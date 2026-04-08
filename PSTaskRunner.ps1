        Add-Type -TypeDefinition @'
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

public sealed class Result<T>
{
    public bool IsOk { get; }
    public T? Value { get; }
    public string? Error { get; }
    public bool IsError => !IsOk;

    private Result(T value)
    {
        IsOk = true;
        Value = value;
        Error = null;
    }

    private Result(string error)
    {
        IsOk = false;
        Error = error;
        Value = default;
    }
    public void Deconstruct(out bool ok, out T? value, out string? error)
    {
        ok = IsOk;
        value = Value;
        error = Error;
    }

    public static Result<T> Ok(T value)
        => new Result<T>(value);

    public static Result<T> ErrorResult(string error)
        => new Result<T>(error);

    public override string ToString()
        => IsOk ? $"Ok({Value})" : $"Error({Error})";
}

public class PowerShellTaskRunner
{
    public static PowerShellTaskRunner Create()
    {
        var iss = InitialSessionState.CreateDefault();
        RunspacePool rsPool = RunspaceFactory.CreateRunspacePool(iss);
        rsPool.SetMinRunspaces(1);
        rsPool.SetMaxRunspaces(5);
        rsPool.Open();
        return new PowerShellTaskRunner(rsPool);
    }
    private readonly RunspacePool _runspacePool;
    protected PowerShellTaskRunner(RunspacePool runspacePool)
    {
        _runspacePool = runspacePool;
    }
    public void RunDefaultCommand()
    {
        using (PowerShell pwsh = GetPowerShellInstance())
        {
            pwsh.AddCommand("Write-Host")
                .AddParameter("Object", "Hello!")
                .Invoke();
        }
    }
    public Collection<string> RunGetModuleListCommand()
    {
        using (PowerShell pwsh = GetPowerShellInstance())
        {
            return pwsh
                .AddCommand("Get-Module")
                    .AddParameter("ListAvailable")
                .AddCommand("Out-String")
                .Invoke<string>();
        }
    }

    public Collection<T> RunCommand<T>(PSCommand psCommand)
    {
        using (PowerShell pwsh = GetPowerShellInstance())
        {
            pwsh.Commands = psCommand;
            return pwsh.Invoke<T>();
        }
    }
    public DataReceivedEventHandler CreateHandler<T>(ScriptBlock sb, BlockingCollection<string> queue)
    {
        return (sender, e) =>
        {

            if (e == null || String.IsNullOrEmpty(e.Data))
                return;
            try
            {
                var results = RunScriptBlock<string, string>(sb, e.Data);
                foreach (var item in results)
                {
                    queue.Add($"{item}");
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine("PsEventBridge handler exception: " + ex);
            }
        };
    }
    public BlockingCollection<Result<TOut>> MapParallel<TIn, TOut>(
    IEnumerable<TIn> inputs,
    ScriptBlock sb)
    {
        var output = new BlockingCollection<Result<TOut>>();
        int remaining = 0;
        
        var settings = new PSInvocationSettings
        {
            // Any settings go here
        };

        var setupPwsh = () =>
        {
            var ps = GetPowerShellInstance();
            return ps.AddScript(sb.ToString());

        };

        foreach (var input in inputs)
        {
            Interlocked.Increment(ref remaining);

            PowerShell pwsh = setupPwsh();

            pwsh.AddArgument(input);

            pwsh.BeginInvoke<PSObject>(
                null,
                settings,
                (IAsyncResult ar) =>
                {
                    try
                    {
                        PSDataCollection<PSObject> results = pwsh.EndInvoke(ar);

                        foreach (PSObject obj in results)
                        {
                            output.Add(Result<TOut>.Ok((TOut)obj.BaseObject));
                        }
                    }
                    catch (OperationCanceledException e)
                    {
                        output.Add(Result<TOut>.ErrorResult("Operation canceled: " + e.Message)); // Add error result to maintain count
                        Console.Error.WriteLine("OperationCanceledException in MapParallel callback: " + e);
                    }
                    catch (Exception e)
                    {
                        output.Add(Result<TOut>.ErrorResult(e.ToString())); // Add error result to maintain count
                        Console.WriteLine("Exception in MapParallel callback: " + e);
                    }
                    finally
                    {
                        pwsh.Dispose();

                        if (Interlocked.Decrement(ref remaining) == 0)
                            output.CompleteAdding();
                        // Console.WriteLine($"remaining={remaining}");
                    }

                },
                state: null);
        }
        if (remaining == 0)
            output.CompleteAdding();
        
        return output;
    }
    public BlockingCollection<Result<TOut>> MapParallelOrdered<TIn, TOut>(
    IList<TIn> inputs,
    ScriptBlock sb)
    {
        var output = new BlockingCollection<Result<TOut>>();
        var results = new ConcurrentDictionary<int, Result<TOut>>();

        int remaining = inputs.Count;
        // int nextIndex = 0;

        for (int i = 0; i < inputs.Count; i++)
        {
            int index = i;
            var input = inputs[i];

            PowerShell pwsh = GetPowerShellInstance();

            pwsh.AddScript(sb.ToString());
            pwsh.AddArgument(input);

            pwsh.BeginInvoke<PSObject>(
                null,
                null,
                ar =>
                {
                    try
                    {
                        var r = pwsh.EndInvoke(ar);

                        foreach (var obj in r)
                        {
                            results[index] = Result<TOut>.Ok((TOut)obj.BaseObject);
                        }
                    }
                    catch (Exception e)
                    {
                        results[index] = Result<TOut>.ErrorResult(e.ToString());
                    }
                    finally
                    {
                        pwsh.Dispose();

                        if (Interlocked.Decrement(ref remaining) == 0)
                        {
                            // emit results in order
                            for (int j = 0; j < inputs.Count; j++)
                            {
                                output.Add(results[j]);
                            }

                            output.CompleteAdding();
                        }
                    }
                },
                null);
        }

        return output;
    }
    public BlockingCollection<Result<TOut>> MapParaStreamOrdered<TIn, TOut>(
    IList<TIn> inputs,
    ScriptBlock sb)
    {
        var output = new BlockingCollection<Result<TOut>>();

        int remaining = inputs.Count;
        var results = new ConcurrentDictionary<int, Result<TOut>>();
        int nextIndex = 0;
        object emitLock = new object();

        for (int i = 0; i < inputs.Count; i++)
        {
            int index = i;
            var input = inputs[i];

            PowerShell pwsh = GetPowerShellInstance();

            pwsh.AddScript(sb.ToString());
            pwsh.AddArgument(input);

            pwsh.BeginInvoke<PSObject>(
                null,
                null,
                ar =>
                {
                    try
                    {
                        var r = pwsh.EndInvoke(ar);

                        foreach (var obj in r)
                        {
                            results[index] = Result<TOut>.Ok((TOut)obj.BaseObject);
                            lock (emitLock)
                            {
                                while (results.TryRemove(nextIndex, out var ready))
                                {
                                    output.Add(ready);
                                    nextIndex++;
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        results[index] = Result<TOut>.ErrorResult(e.ToString());

                        lock (emitLock)
                        {
                            while (results.TryRemove(nextIndex, out var ready))
                            {
                                output.Add(ready);
                                nextIndex++;
                            }
                        }
                    }
                    finally
                    {
                        pwsh.Dispose();

                        if (Interlocked.Decrement(ref remaining) == 0)
                        {
                            // emit results in order
                            // for (int j = 0; j < inputs.Count; j++)
                            // {
                            //     output.Add(results[j]);
                            // }

                            output.CompleteAdding();
                        }
                    }
                },
                null);
        }

        return output;
    }
    public Collection<T> RunScriptBlock<S, T>(ScriptBlock sb, S input)
    {
        PSCommand command = new PSCommand();
        command.AddScript(sb.ToString());
        if (input != null)
        {
            command.AddArgument(input);
        }
        return RunCommand<T>(command);
    }
    public Task<Collection<T>> RunScriptBlockAsync<S, T>(ScriptBlock sb, S input)
    {
        PSCommand command = new PSCommand();
        command.AddScript(sb.ToString());
        if (input != null)
        {
            command.AddArgument(input);
        }
        return RunCommandAsync<T>(command);
    }
    public Task<Collection<T>> RunCommandAsync<T>(PSCommand psCommand)
    {
        return Task.Run(() =>
        {
            using (PowerShell pwsh = GetPowerShellInstance())
            {
                pwsh.Commands = psCommand;
                return pwsh.Invoke<T>();
            }
        });
    }


    private PowerShell GetPowerShellInstance()
    {
        var pwsh = PowerShell.Create(RunspaceMode.NewRunspace);
        pwsh.RunspacePool = _runspacePool;
        return pwsh;
    }
}
'@
