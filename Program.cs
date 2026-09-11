using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using StreamUllrIO;

public delegate object? JobCallback(ReadOnlyMemory<byte> payload);

public struct Job
{
    public int JobId;
    public int FunctionId;
    public byte[] payload;
};

public struct JobResult
{
    public int JobId;
    public object? Result;
};

public struct Mac
{
    public string Name;
};

public struct RegisteredFunction
{
    public int FunctionId; //To find it in the array
    public JobCallback Callback; //Function execution
    public Delegate DelegateInfer;
};

public class JobHandlerAttrAttribute : Attribute { }


//TODO(ullr): Implement C# source generators for maximum efficiency at compile-time
public static class JobHandler
{
    public static RegisteredFunction[] functionTable = Array.Empty<RegisteredFunction>();

    private static readonly Dictionary<Type, object?> InstanceCache = new();

    private static byte[] payloadArr = new byte[1024];

    private static int _nextJobId = 0;


    //NOTE: Concurrenc and Workers
    private static ConcurrentQueue<Job> jobQueue = new();
    private static readonly object queueLock = new();
    private static bool isRunning = true;

    public static int Main(string[] args)
    {
        List<RegisteredFunction> functions = new List<RegisteredFunction>();
        StoreAttributeFuncMetadata(functions);
        functionTable = functions.ToArray();
        StartWorkers(2);

        CreateJob(Add, 1,2);
        Mac mac = new Mac{Name = "cas"};
        var macs = new List<Mac> {mac};
        CreateJob(StringPrueba, "hola", macs);

        return 0;
    }

    private static void StoreAttributeFuncMetadata(List<RegisteredFunction> functions)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

        int functionId = 0;
        foreach (Assembly assembly in assemblies)
        {
            foreach (Type type in assembly.GetTypes())
            {
                foreach (MethodInfo function in type.GetMethods(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Static |
                    BindingFlags.Instance ))
                {
                    var attribute = function.GetCustomAttribute<JobHandlerAttrAttribute>();
                    if (attribute == null)
                        continue;

                    var instance = function.IsStatic? null : GetOrCreateInstance(function.DeclaringType!)!;
                    //Creates the delegates to store the signature to compare when the user do foo(bar, args);
                    Type[] parameterTypes = function
                        .GetParameters()
                        .Select(p => p.ParameterType)
                        .Concat(new[] {function.ReturnType})
                        .ToArray();
                    Type delegateType = Expression.GetDelegateType(parameterTypes);

                    RegisteredFunction registered;
                    registered.FunctionId = functionId++;
                    registered.Callback = JobCallbackAdapter(function, instance);
                    registered.DelegateInfer = function.CreateDelegate(delegateType, instance);
                    functions.Add(registered);
                }
            }
        }
    }

    private static JobCallback JobCallbackAdapter(MethodInfo functionInfo, object? instanceInfo)
    {

        ParameterInfo[] parameters = functionInfo.GetParameters();

        var payload = Expression.Parameter(typeof(ReadOnlyMemory<byte>), "payload");
        var stream = Expression.Variable(typeof(UllrMemoryStream), "stream");

        var body = new List<Expression>();

        body.Add(
            Expression.Assign(
                stream,
                Expression.New(
                    typeof(UllrMemoryStream).GetConstructor(new []{typeof(byte[])})!,
                    Expression.Call(
                        payload,
                        nameof(ReadOnlyMemory<byte>.ToArray),
                        null
                    )
                )
            )
        );

        var args = new Expression[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            args[i] = BuildReadExpression(parameters[i].ParameterType, stream);
        }

        Expression instanceExpression = 
            instanceInfo == null 
                ? null! 
                : Expression.Constant(
                        instanceInfo,
                        functionInfo.DeclaringType!);

        var call = Expression.Call(
                    instanceExpression,
                    functionInfo,
                    args);

        if (functionInfo.ReturnType == typeof(void))
        {
            body.Add(call);
            body.Add(
                Expression.Constant(
                    null,
                    typeof(object)));
        }
        else
        {
            body.Add(
                Expression.Convert(
                    call,
                    typeof(object)));
        }

        var block = Expression.Block(
                    new[] {stream},
                    body);

        return Expression
               .Lambda<JobCallback>(
                    block,
                    payload)
               .Compile();

    }

    private static object? GetOrCreateInstance(Type type)
    {
        if (type.IsAbstract || type.IsInterface)
            throw new InvalidOperationException($"Cannot create instance of {type}");
        
        if (!InstanceCache.TryGetValue(type, out var instance))
        {
            instance = Activator.CreateInstance(type);
            InstanceCache[type] = instance;
        }

        return instance;
    }

    public static Expression BuildReadExpression(Type type, Expression stream)
    {
        MethodInfo deserializeMethod =
            typeof(UllrIO)
                .GetMethod(nameof(UllrIO.Deserialize))!
                .MakeGenericMethod(type);

        return Expression.Call(
                deserializeMethod,
                stream);
    }

    //TODO(ullr): Create the jobId and ResultQueue implementation
    private static void CreateJob<TDelegate>(TDelegate callback, params object[] args) where TDelegate : Delegate
    {
        UllrMemoryStream stream = new();

        foreach (var arg in args)
        {
            UllrIO.Serialize(stream, arg);
        }

       foreach (var registered in functionTable) 
       {
           if (registered.DelegateInfer == callback)
           {
               int jobId = Interlocked.Increment(ref _nextJobId);
               Job job = new();
               job.JobId = jobId;
               job.FunctionId = registered.FunctionId;
               job.payload = stream.ToArray();

               jobQueue.Enqueue(job);

               lock(queueLock)
               {
                   Monitor.Pulse(queueLock);
               }
               break;
           }
       }
    }

    private static void StartWorkers(int workersNum)
    {
        isRunning = true;
        AppDomain.CurrentDomain.ProcessExit += (sender, e) => OnClientProcessExit();
        for (int i = 0; i < workersNum; i++)
        {
            int workerId = i;
            Thread t = new Thread(() => WorkerLoop(workerId)) {IsBackground = true};
            t.Start();
        }
        Console.WriteLine("Workers initialized");
    }

    private static void WorkerLoop(int workerId)
    {
        while (isRunning)
        {
            Job job;
            bool hasJob = false;

            lock (queueLock)
            {
                while (jobQueue.IsEmpty && isRunning)
                {
                    Monitor.Wait(queueLock);
                }

                if(!isRunning) break;

                hasJob = jobQueue.TryDequeue(out job);
            }
            if (hasJob)
            {
                try
                {
                    var registered = functionTable[job.FunctionId];
                    var payloadSegment = new ReadOnlyMemory<byte>(job.payload, 0, job.payload.Length);
                    registered.Callback(payloadSegment);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Worker {workerId} Error: {ex.Message}");
                }
            }
        }
    }

    public static void OnClientProcessExit()
    {
        lock (queueLock)
        {
            isRunning = false;

            Monitor.PulseAll(queueLock);
        }
        Console.WriteLine("Closing all workers");
    }

    //TODO(ullr): Create the woker and queue functionality
    
    [JobHandlerAttr]
    private static int Add(int x, int y)
    {
        Console.WriteLine(x+y);
        return x + y;
    }

    [JobHandlerAttr]
    private static void StringPrueba(string cad , List<Mac> y)
    {
        Console.WriteLine(cad);
        foreach (var x in y)
            Console.WriteLine(x.Name);
    }
};
