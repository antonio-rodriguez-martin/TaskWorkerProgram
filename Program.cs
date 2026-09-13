using System.Buffers;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using StreamUllrIO;

namespace UllrJobHandler
{
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
        public Exception? Error;
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
        private static RegisteredFunction[] functionTable = Array.Empty<RegisteredFunction>();

        private static readonly Dictionary<Type, object?> InstanceCache = new();
        private static int _nextJobId = 0;
        private static bool singleton = false;


        //NOTE: Concurrent and Workers
        private static ConcurrentQueue<Job> jobQueue = new();
        private static readonly object queueLock = new();
        private static bool isRunning = true;

        //NOTE: Concurrent exit queue
        private static ConcurrentQueue<JobResult> resultQueue = new();
        private static readonly ConcurrentDictionary<int, JobResult> _completedJobs = new();

        public static void InitJobHandler(int workersNumber)
        {
            if(!singleton)
            {
                List<RegisteredFunction> functions = new List<RegisteredFunction>();
                StoreAttributeFuncMetadata(functions);
                functionTable = functions.ToArray();
                StartWorkers(workersNumber);
                singleton = true;
            }
        }

        private static void StoreAttributeFuncMetadata(List<RegisteredFunction> functions)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            int functionId = 0;
            foreach (Assembly assembly in assemblies)
            {
                try
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
                catch (ReflectionTypeLoadException)
                {
                    Console.WriteLine("Failed to load assembly type");
                    continue;
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

        //NOTE(ullr): To be able to create the instance of the class correctly
        //it must have a parameterless constructor
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

        private static Expression BuildReadExpression(Type type, Expression stream)
        {
            MethodInfo deserializeMethod =
                typeof(UllrIO)
                    .GetMethod(nameof(UllrIO.Deserialize))!
                    .MakeGenericMethod(type);

            return Expression.Call(
                    deserializeMethod,
                    stream);
        }

        public static int CreateJob<TDelegate>(TDelegate callback, params object[] args) where TDelegate : Delegate
        {
            UllrMemoryStream stream = new();

            var parameters = callback.Method.GetParameters();

            //NOTE(ullr): For default parameters this will throw an error anyway
            if (parameters.Length != args.Length)
                throw new ArgumentException("Argument count does not match function parameters");

            //Gets the argument from the function param and it's type to serialize checking
            //between nullables or not
            for (int i = 0; i < args.Length; i++)
            {
                var value = args[i];
                var declaredType = parameters[i].ParameterType;
                UllrIO.Serialize(stream, value, declaredType);
            }

           foreach (var registered in functionTable) 
           {

               if(registered.DelegateInfer.Method == callback.Method && registered.DelegateInfer.Target == callback.Target)
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
                   return jobId;
               }
           }
           throw new InvalidOperationException("Function Not Registered");
        }

        public static void StartWorkers(int workersNum)
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

                        object? executionResult = registered.Callback(payloadSegment);

                        JobResult result = new JobResult
                        {
                            JobId = job.JobId,
                            Result = executionResult,
                            Error = null
                        };
                        resultQueue.Enqueue(result);

                    }
                    catch (Exception ex)
                    {
                        resultQueue.Enqueue(new JobResult
                        {
                            JobId = job.JobId,
                            Result = null,
                            Error = ex
                        });
                    }
                }
            }
        }

        public static bool TryGetResult<T>(int jobId, out T? result, out Exception? error)
        {
            while (resultQueue.TryDequeue(out JobResult jobResult))
            {
                _completedJobs[jobResult.JobId] = jobResult;
            }

            if (_completedJobs.TryRemove(jobId, out var entry))
            {
                error = entry.Error;

                if (entry.Result is T t)
                {
                    result = t;
                    return true;
                }
                if (entry.Result == null && default(T) == null)
                {
                    result = default;
                    return true;
                }

                result = default;
                return true;
            }
            result = default;
            error = null;
            return false;
        }

        public static bool TryGetResult<T>(int jobId, out T? result)
            => TryGetResult<T>(jobId, out result, out _);

        private static void OnClientProcessExit()
        {
            lock (queueLock)
            {
                isRunning = false;

                Monitor.PulseAll(queueLock);
            }
            Console.WriteLine("Closing all workers");
        }
    };
}
