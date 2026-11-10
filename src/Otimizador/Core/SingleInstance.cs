namespace Otimizador.Core;

/// <summary>Garante uma única instância. Uma segunda execução apenas traz a janela existente à frente.</summary>
internal static class SingleInstance
{
    private const string MutexName = @"Local\OtimizadorPCPro_Instancia";
    private const string EventName = @"Local\OtimizadorPCPro_Mostrar";
    private static Mutex? _mutex;
    private static EventWaitHandle? _event;
    private static RegisteredWaitHandle? _wait;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out bool created);
        if (created) return true;
        try
        {
            if (EventWaitHandle.TryOpenExisting(EventName, out var ev)) { ev.Set(); ev.Dispose(); }
        }
        catch { }
        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    /// <summary>Executa <paramref name="onShowRequest"/> quando outra instância pedir para mostrar a janela.</summary>
    public static void Listen(Action onShowRequest)
    {
        _event = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        _wait = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => onShowRequest(), null, Timeout.Infinite, false);
    }

    public static void Release()
    {
        try { _wait?.Unregister(null); } catch { }
        try { _event?.Dispose(); } catch { }
        try { _mutex?.ReleaseMutex(); _mutex?.Dispose(); } catch { }
        _wait = null; _event = null; _mutex = null;
    }
}
