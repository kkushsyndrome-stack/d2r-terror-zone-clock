using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace SetSystemTime
{
internal static class Program
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetLocalTime(ref SystemTime systemTime);

    [STAThread]
    private static void Main(string[] args)
    {
        var input = args.Length > 0
            ? string.Join(" ", args)
            : PromptForDateTime();

        DateTime requested;
        if (!DateTime.TryParse(input, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out requested))
        {
            Console.Error.WriteLine("Invalid date/time. Example: 2026-09-15 14:30:00");
            Environment.ExitCode = 1;
            return;
        }

        var systemTime = new SystemTime
        {
            Year = (ushort)requested.Year,
            Month = (ushort)requested.Month,
            Day = (ushort)requested.Day,
            Hour = (ushort)requested.Hour,
            Minute = (ushort)requested.Minute,
            Second = (ushort)requested.Second,
            Milliseconds = (ushort)requested.Millisecond
        };

        if (!SetLocalTime(ref systemTime))
        {
            var error = Marshal.GetLastWin32Error();
            Console.Error.WriteLine(string.Format("Windows could not set the local time (error {0}).", error));
            Environment.ExitCode = error;
            return;
        }

        Console.WriteLine(string.Format("System time set to {0:yyyy-MM-dd HH:mm:ss}.", requested));
    }

    private static string PromptForDateTime()
    {
        Console.Write("Enter local date/time (example: 2026-09-15 14:30:00): ");
        return Console.ReadLine() ?? string.Empty;
    }
}
}