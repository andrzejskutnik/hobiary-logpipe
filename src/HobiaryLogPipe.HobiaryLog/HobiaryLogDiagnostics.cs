using System.Runtime.InteropServices;

namespace HobiaryLogPipe.HobiaryLog;

public static class HobiaryLogDiagnostics
{
    public const string NativeLibraryName = "hobiarylogdb";

    public static string BuildDirectoryPreparationFailureMessage(string dir, Exception ex)
    {
        return $"Nie można przygotować katalogu logu '{dir}' (resolved: '{TryGetFullPath(dir)}'). " +
               $"Powód: {ex.GetType().Name}: {ex.Message}. " +
               "Sprawdź poprawność ścieżki, uprawnienia procesu oraz możliwość tworzenia katalogów.";
    }

    public static string BuildOpenFailureMessage(string dir)
    {
        return $"Nie można otworzyć logu HobiaryLogDB dla katalogu '{dir}' (resolved: '{TryGetFullPath(dir)}'). " +
               "Natywne hlog_open zwróciło null. Sprawdź, czy katalog jest dostępny do zapisu, " +
               $"czy biblioteka {NativeLibraryName}.dll/lib{NativeLibraryName}.so jest obecna w output projektu " +
               "oraz czy jej architektura zgadza się z procesem .NET.";
    }

    public static string BuildNativeLibraryLoadFailureMessage(Exception ex)
    {
        return $"Nie można załadować biblioteki natywnej '{NativeLibraryName}' z katalogu aplikacji '{AppContext.BaseDirectory}'. " +
               $"Powód: {ex.GetType().Name}: {ex.Message}. " +
               "Sprawdź obecność pliku DLL/SO w output projektu oraz zgodność architektury x64/x86/ARM procesu i biblioteki.";
    }

    public static string BuildAppendFailureMessage(int result, string dir, uint payloadBytes)
    {
        return $"Nie można zapisać rekordu do HobiaryLogDB w katalogu '{dir}' (resolved: '{TryGetFullPath(dir)}'). " +
               $"Natywne hlog_append zwróciło kod {result}. Payload bytes: {payloadBytes}. {DescribeAppendFailure(result)}";
    }

    private static string DescribeAppendFailure(int result)
    {
        return result switch
        {
            -1 => "Najczęstsze przyczyny: problem z zapisem natywnym, niedostępny katalog logu, konflikt dostępu do pliku albo błąd formatu danych wejściowych.",
            _ => "Sprawdź uprawnienia do katalogu, dostępność plików segmentów i to, czy natywna biblioteka działa poprawnie w bieżącym środowisku."
        };
    }

    private static string TryGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}