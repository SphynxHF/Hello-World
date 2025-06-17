// ---------------------------------------------------------------------------------------------------------------------
//  HelloWorldFramework – a wildly over-complicated console “Hello, <name>” sample
//  Target framework: net7.0 (or net8.0)
// ---------------------------------------------------------------------------------------------------------------------
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace HelloWorldFramework
{
    // ====== Infrastructure ==========================================================================================

    /// <summary>Simple IoC / service locator to keep the sample self-contained.</summary>
    public static class ServiceLocator
    {
        private static readonly ConcurrentDictionary<Type, object> _services = new();

        public static void Register<TService>(TService implementation) where TService : notnull =>
            _services[typeof(TService)] = implementation;

        public static TService Resolve<TService>() where TService : notnull =>
            (TService)(_services.TryGetValue(typeof(TService), out var impl)
                ? impl
                : throw new InvalidOperationException($"Service {typeof(TService).Name} not registered"));
    }

    /// <summary>Thin async/await-friendly wrapper around <see cref="Console"/>.</summary>
    public interface IConsoleFacade
    {
        ValueTask WriteAsync(string text, CancellationToken token = default);
        ValueTask WriteLineAsync(string text, CancellationToken token = default);
        ValueTask<string?> ReadLineAsync(CancellationToken token = default);
    }

    public sealed class StandardConsoleFacade : IConsoleFacade
    {
        public ValueTask WriteAsync(string text, CancellationToken token = default) =>
            ValueTask.CompletedTask.ContinueWith(_ => Console.Write(text), token);

        public ValueTask WriteLineAsync(string text, CancellationToken token = default) =>
            ValueTask.CompletedTask.ContinueWith(_ => Console.WriteLine(text), token);

        public ValueTask<string?> ReadLineAsync(CancellationToken token = default) =>
            new(() => Console.ReadLine());
    }

    /// <summary>Very small event bus for decoupled pub/sub inside the process.</summary>
    public interface IEventBus
    {
        ValueTask PublishAsync<TEvent>(TEvent evt, CancellationToken cancel = default);
        IAsyncEnumerable<object> EventsAsync(CancellationToken cancel = default);
    }

    public sealed class InMemoryEventBus : IEventBus
    {
        private readonly Channel<object> _channel = Channel.CreateUnbounded<object>();

        public async ValueTask PublishAsync<TEvent>(TEvent evt, CancellationToken cancel = default) =>
            await _channel.Writer.WriteAsync(evt!, cancel);

        public IAsyncEnumerable<object> EventsAsync(CancellationToken cancel = default) =>
            _channel.Reader.ReadAllAsync(cancel);
    }

    // ====== Result wrapper (because why not) =========================================================================

    public readonly record struct Result<T>(bool IsSuccess, T? Value, Exception? Error)
    {
        public static Result<T> Success(T value) => new(true, value, null);
        public static Result<T> Failure(Exception ex) => new(false, default, ex);
        public void Deconstruct(out bool ok, out T? value, out Exception? error) => (ok, value, error) = (IsSuccess, Value, Error);
    }

    // ====== Command framework =======================================================================================

    public interface ICommand
    {
        Task<Result<Unit>> ExecuteAsync(CancellationToken cancel = default);
    }

    /// <summary> Marker attribute so reflection can locate the “primary” command. </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class PrimaryCommandAttribute : Attribute { }

    /// <summary>Unit type for commands that return nothing.</summary>
    public readonly record struct Unit
    {
        public static readonly Unit Value = new();
        public override string ToString() => "()";
    }

    public static class CommandRunner
    {
        public static async Task<int> RunPrimaryAsync(CancellationToken cancel = default)
        {
            var primaryCmdType = Assembly.GetExecutingAssembly()
                                         .GetTypes()
                                         .FirstOrDefault(t => t.GetCustomAttribute<PrimaryCommandAttribute>() != null)
                                 ?? throw new InvalidOperationException("No [PrimaryCommand] found.");

            if (Activator.CreateInstance(primaryCmdType) is not ICommand cmd)
                throw new InvalidOperationException("Primary command must implement ICommand.");

            var (ok, _, err) = await cmd.ExecuteAsync(cancel);

            if (!ok && err != null)
            {
                await ServiceLocator.Resolve<IConsoleFacade>()
                                    .WriteLineAsync($"Unhandled error: {err}", cancel);
                return 2;
            }
            return 0;
        }
    }

    // ====== Events (utterly overkill for one event) ==================================================================

    public sealed record NameEnteredEvent(string Name);

    // ====== The “business logic” command ============================================================================

    [PrimaryCommand]
    public sealed class GreetUserCommand : ICommand
    {
        private readonly IConsoleFacade _console = ServiceLocator.Resolve<IConsoleFacade>();
        private readonly IEventBus _bus     = ServiceLocator.Resolve<IEventBus>();

        public async Task<Result<Unit>> ExecuteAsync(CancellationToken cancel = default)
        {
            try
            {
                await _console.WriteAsync("Enter your name: ", cancel);
                var name = await _console.ReadLineAsync(cancel) ?? "<null>";

                // Publish an event nobody listens to—just for show.
                await _bus.PublishAsync(new NameEnteredEvent(name), cancel);

                await _console.WriteLineAsync($"Hello, World! And hello, {name}!", cancel);
                await _console.WriteLineAsync("Press Enter to terminate…", cancel);
                await _console.ReadLineAsync(cancel);

                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex)
            {
                return Result<Unit>.Failure(ex);
            }
        }
    }

    // ====== Program entry-point ======================================================================================

    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            // Wire up our “container”
            ServiceLocator.Register<IConsoleFacade>(new StandardConsoleFacade());
            ServiceLocator.Register<IEventBus>(new InMemoryEventBus());

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            // Optional: Background listener that logs every event to Debug out
            _ = Task.Run(async () =>
            {
                await foreach (var evt in ServiceLocator.Resolve<IEventBus>().EventsAsync(cts.Token))
                {
                    Debug.WriteLine($"[EVENT] {evt}");
                }
            });

            return await CommandRunner.RunPrimaryAsync(cts.Token);
        }
    }
}