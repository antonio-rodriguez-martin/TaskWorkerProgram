# UllrJobHandler

UllrJobHandler is an in-process job dispatcher using attributes to mark functions/methods with [JobHandlerAttr]. Inside the main executing block of code there must be a call to **InitJobHandler** once at startup, then a job can be submited by passing a delegate to **CreateJob**.
The workers pull jobs off a queue, deserialize arguments, invoke the handler and publish the result. The caller polls **TryGetResult** to retrieve if necessary.
It is dessigned for fire-and-poll workloads: many small, independent tasks with serializable inputs and outputs.

### Usage Guide

## 1. Setup

Call **InitJobHandler** exactly once, before any **CreateJob**. It scans all loaded assemblies for [JobHandlerAttr] methods, builds the dispatch table, and starts the worker pool. Calling it more than once is a no-op.
```
using UllrJobHandler;

public static class Program
{
    public static void Main()
    {
        JobHandler.InitJobHandler(workersNumber: 4);
        // ... submit jobs ...
    }
}
```
## 2. Defining a handler

Any static or instance methods, public or non-public, with any signature, can be a handler. Mark it with [JobHandlerAttr].
```
public static class MathHandlers
{
    [JobHandlerAttr]
    public static int Add(int a, int b) => a + b;

    [JobHandlerAttr]
    public static double Average(int[] values)
    {
        double sum = 0;
        foreach (var v in values) sum += v;
        return values.Length == 0 ? 0 : sum / values.Length;
    }

    [JobHandlerAttr]
    public static void Log(string message)
        => Console.WriteLine(message);
}
```
Parameters and return types must be serializable by UllrIO: primitives, string, enums, arrays, List<T>, structs and classes with a parameterless constructor. Nullable value types (int?) are supported.
Instance handlers must have a public parameterless constructor. The library creates and caches one instance per type, shared across all workers.
**Because a single instance is shared by all workers, instance handlers must be thread-sage if they mutate state**

## 3. Submitting a job

Pass a delegate that points to a registered handler, followed by the arguments. CreateJob returns a job ID inmediatly.

```
int jobId = JobHandler.CreateJob<Func<int, int, int>>(MathHandlers.Add, 2, 40);
///
int JobId = JobHandler.CreateJob(MathHandlers.Add, 2, 40); // also works
```

Any delegate type can be used, built-in or custom, as long as it points to the same method. Matching is done by method and target, not delegate type.

```
public delegate int MyAdd(int a, int b);

JobHandler.CreateJob<MyAdd>(MathHandlers.Add, 2, 40); // also works
```

Argument count is checked at call time. Passing the wrong number of arguments throws **ArgumentException=**before the job is queued.
Unregistered methods throw =**nvalidOperationException=** The method must be marked [JobHandlerAttr] and discovered at startup.

## 4. Retrieving a result

Jobs run asynchronously. Poll TryGetResult until it returns true.
```
int jobId = JobHandler.CreateJob<Func<int, int, int>>(MathHandlers.Add, 2, 40);

int result;
Exception? error;
while (!JobHandler.TryGetResult<int>(jobId, out result, out error))
{
    Thread.Sleep(5); // or do other work
}

if (error != null)
    Console.WriteLine($"Job failed: {error.Message}");
else
    Console.WriteLine($"Result: {result}");
```
 
##### *Semantics of return value*
| Return              | Meaning                                                 |
|---------------------|---------------------------------------------------------|
| false               | Job has not finished yet.                               |
| true, error == null | Job succeeded. result holds the value.                  |
| true, error != null | Job completed but the handler threw. result is default. |

The two-argument overload ignores errors
```
if (JobHandler.TryGetResult<int>(jobId, out var value))
{
    // true = finished (success OR failure); check separately if you care
}
```
*Important*: the two-argument overload returns true for failed jobs too, with value = default. Use the three-argument form if you need to distinguish success from failure.

5. Typical pattern: submit many, collect later

The common case is submitting a batch of jobs, then collecting results.
```
var ids = new int[1000];
for (int i = 0; i < ids.Length; i++)
    ids[i] = JobHandler.CreateJob(MathHandlers.Add, i, 1);

long total = 0;
for (int i = 0; i < ids.Length; i++)
{
    int value;
    Exception? error;
    while (!JobHandler.TryGetResult<int>(ids[i], out value, out error))
        Thread.Sleep(1);

    if (error != null)
        throw new Exception($"Job {ids[i]} failed", error);

    total += value;
}
```

## 6. Handling failures

A handler that throws does not crash the worker. The exception is captured and delivered through the error out-parameter.

```
[JobHandlerAttr]
public static int Divide(int a, int b)
{
    if (b == 0) throw new DivideByZeroException();
    return a / b;
}
```
```
int jobId = JobHandler.CreateJob<Func<int, int, int>>(Divide, 10, 0);

int value;
Exception? error;
while (!JobHandler.TryGetResult<int>(jobId, out value, out error))
    Thread.Sleep(5);

// error is a DivideByZeroException
```

## 7. Void handlers

void handlers complete normally with a null result. Retrieve then with object? or object.

```
[JobHandlerAttr]
public static void Notify(string msg) => Console.WriteLine(msg);
```
```
int jobId = JobHandler.CreateJob<Action<string>>(Notify, "hello");

object? ignored;
Exception? error;
while (!JobHandler.TryGetResult<object?>(jobId, out ignored, out error))
    Thread.Sleep(5);
```

## 8. Supported parameters and return types

| Category            | Supported                                                                     |
|---------------------|-------------------------------------------------------------------------------|
| Primitives          | byte, sbyte, bool, short, ushort, int, uint, long, ulong, float, double, char |
| Nullable            | T? for any supported value type                                               |
| Strings             | string                                                                        |
| Enums               | any enum                                                                      |
| Collections         | T[], List<T>                                                                  |
| Structs and classes | public and non-public instance fields, recursively                            |
| Null                | reference types and Nullable<T> may be null                                   |


*Not supported*: interfaces, abstract classes, polymorphysm (the declared type is used instead of the runtim type), circular references, Dictionariy<K,V> and null elements inside arrays or lists.

## 9. Constraints

Initialize once. InitJobHandler is idempotent; call it at process startup.

Handlers must be registered before first use. Registration happens during InitJobHandler.

Instance handlers are shared. One instance per type, called concurrently by all workers. Make them thread-safe.

Instance handlers need a public parameterless constructor.

Lambdas and closures don't match. CreateJob matches by method and target. A lambda like (a,b) => Add(a,b) compiles to a different method and will throw "Function Not Registered". Pass the method group directly: Add.

Static state. The library is process-global. Only one job system exists per process.

No cancellation or timeout. Once submitted, a job runs to completion (or throws).

No async handlers. A handler returning Task is boxed as-is; the task is not awaited.

## 10. Simple example of use

```
using System;
using System.Threading;
using UllrJobHandler;

public static class Handlers
{
    [JobHandlerAttr]
    public static int Square(int x) => x * x;

    [JobHandlerAttr]
    public static string Greet(string name) => $"Hello, {name}!";

    [JobHandlerAttr]
    public static int Fail(int x) => throw new InvalidOperationException("nope");
}

public static class Program
{
    public static void Main()
    {
        JobHandler.InitJobHandler(workersNumber: 4);

        int a = JobHandler.CreateJob<Func<int, int>>(Handlers.Square, 7);
        int b = JobHandler.CreateJob<Func<string, string>>(Handlers.Greet, "world");
        int c = JobHandler.CreateJob<Func<int, int>>(Handlers.Fail, 1);

        Print<int>(a);
        Print<string>(b);
        Print<int>(c);
    }

    private static void Print<T>(int jobId)
    {
        T value;
        Exception? error;
        while (!JobHandler.TryGetResult<T>(jobId, out value, out error))
            Thread.Sleep(5);

        if (error != null)
            Console.WriteLine($"Job {jobId} failed: {error.Message}");
        else
            Console.WriteLine($"Job {jobId} = {value}");
    }
}
```
```
//Output
Job 1 = 49
Job 2 = Hello, world!
Job 3 failed: nope
```
