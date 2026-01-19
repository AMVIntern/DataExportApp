using System;
using System.IO;
using System.Threading;
using System.Text.Json;
using System.Linq;

class Program
{
    private static ReportStateService _reportStateService;
    private static EmailViewModel _viewModel;
    private const int SafetyNetCheckIntervalSeconds = 45; // Check every 45 seconds

    static void Main(string[] args)
    {
        // Load configuration from JSON file
        string configPath = @"C:\AMV\Gocator\Config\config.json"; // Adjust path as needed
        EmailData emailData = LoadConfig(configPath);
        if (emailData == null)
        {
            Console.WriteLine("Failed to load configuration. Exiting.");
            return;
        }

        // Initialize ViewModel
        _viewModel = new EmailViewModel(emailData);

        // Initialize ReportStateService
        string reportStatePath = @"C:\AMV\Gocator\report_state.json";
        _reportStateService = new ReportStateService(reportStatePath);

        // Check for pending reports on startup
        Console.WriteLine("Checking for pending reports on startup...");
        ProcessPendingReports();

        // Run in an infinite loop to wait for scheduled times
        while (true)
        {
            var now = DateTime.Now;

            // Find the next scheduled time
            DateTime nextScheduledTime = GetNextScheduledTime(now);

            // Check if the next scheduled time falls on a Saturday or Sunday
            while (nextScheduledTime.DayOfWeek == DayOfWeek.Saturday || nextScheduledTime.DayOfWeek == DayOfWeek.Sunday)
            {
                // Move to the next Monday by adding days (6 if Sunday, 1 if Saturday)
                now = nextScheduledTime.AddDays(1);
                //nextScheduledTime = GetNextScheduledTime(now, scheduledTimeSpans);
            }

            // Calculate initial delay to next scheduled time
            TimeSpan delay = nextScheduledTime - now;
            Console.WriteLine($"Next report scheduled at {nextScheduledTime}. Waiting for {delay.TotalMinutes:F2} minutes...");

            // Update counter and check safety net periodically until the scheduled time
            while (now < nextScheduledTime)
            {
                TimeSpan remaining = nextScheduledTime - DateTime.Now;
                Console.Write($"\rRemaining time: {remaining.TotalMinutes:F2} minutes "); // \r for overwrite
                
                // Check safety net for pending reports
                ProcessPendingReports();
                
                Thread.Sleep(SafetyNetCheckIntervalSeconds * 1000); // Check every 45 seconds
                now = DateTime.Now;
            }
            Console.WriteLine(); // New line after countdown

            // Generate combined CSV and send email (shift and date from CSV)
            ProcessScheduledReport(nextScheduledTime);
        }
    }

    private static void ProcessPendingReports()
    {
        try
        {
            var pendingReports = _reportStateService.GetPendingReports(DateTime.Now);
            
            foreach (var report in pendingReports)
            {
                Console.WriteLine($"\nProcessing pending report: {report.FileName} (scheduled: {report.ScheduledTime})");
                
                var result = _viewModel.GenerateAndSendReport(report.FileName);
                
                if (result.Success)
                {
                    // Use the actual filename returned from EmailViewModel
                    string actualFileName = result.FileName ?? report.FileName;
                    _reportStateService.MarkReportAsSent(actualFileName, report.ScheduledDateTime);
                    Console.WriteLine($"Successfully processed and marked as sent: {actualFileName}");
                }
                else
                {
                    Console.WriteLine($"Failed to process report: {report.FileName}. Will retry on next check.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing pending reports: {ex.Message}");
        }
    }

    private static void ProcessScheduledReport(DateTime scheduledTime)
    {
        try
        {
            // Process the report without specifying filename - let EmailViewModel find the files
            Console.WriteLine($"Processing scheduled report at {scheduledTime}");
            var result = _viewModel.GenerateAndSendReport();

            if (result.Success && !string.IsNullOrEmpty(result.FileName))
            {
                // Get the actual filename from EmailViewModel
                string fileName = result.FileName;

                // Ensure JSON entry exists for this scheduled time
                _reportStateService.EnsureReportEntry(scheduledTime, fileName);

                // Update JSON entry on success
                _reportStateService.UpdateReportEntryAfterSend(scheduledTime, fileName, true);
                Console.WriteLine($"Successfully processed scheduled report: {fileName}");
            }
            else
            {
                // If processing failed but we got a filename, create entry so safety net can retry
                if (!string.IsNullOrEmpty(result.FileName))
                {
                    string fileName = result.FileName;
                    _reportStateService.EnsureReportEntry(scheduledTime, fileName);
                    // Keep sendMail as false so safety net can retry
                    Console.WriteLine($"Failed to process scheduled report: {fileName}. Will be retried by safety net.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing scheduled report: {ex.Message}");
        }
    }

    private static string GetShiftFromTime(DateTime time)
    {
        int hour = time.Hour;
        if (hour >= 6 && hour < 14) return "1"; // 6 AM to 2 PM
        else if (hour >= 14 && hour < 22) return "2"; // 2 PM to 10 PM
        else return "3"; // 10 PM to 6 AM
    }

    private static DateTime GetNextScheduledTime(DateTime now)
    {
        while (true)
        {
            var day = now.DayOfWeek;

            // Saturday: skip entirely
            if (day == DayOfWeek.Saturday)
            {
                now = now.Date.AddDays(1); // move to Sunday 00:00
                continue;
            }

            // Sunday: only allow 22:00
            if (day == DayOfWeek.Sunday)
            {
                DateTime sundaySlot = now.Date.AddHours(22);

                if (sundaySlot > now)
                    return sundaySlot;

                // Sunday 22:00 already passed → move to Monday
                now = now.Date.AddDays(1);
                continue;
            }

            // Weekdays (Mon–Fri): 06:00, 14:00, 22:00
            TimeSpan[] weekdaySlots =
            {
            new TimeSpan(6, 0, 0),
            new TimeSpan(14, 0, 0),
            new TimeSpan(22, 0, 0)
        };

            foreach (var slot in weekdaySlots)
            {
                DateTime candidate = now.Date + slot;
                if (candidate > now)
                    return candidate;
            }

            // All slots today passed → move to next day
            now = now.Date.AddDays(1);
        }
    }


    private static string GetCurrentShift(DateTime now)
    {
        int hour = now.Hour;
        if (hour >= 6 && hour < 14) return "1"; // 6 AM to 2 PM
        else if (hour >= 14 && hour < 22) return "2"; // 2 PM to 10 PM
        else return "3"; // 10 PM to 6 AM
    }

    private static EmailData LoadConfig(string configPath)
    {
        string jsonString = null; // Declare outside try block for reuse
        try
        {
            if (!File.Exists(configPath))
            {
                // Create directory if it doesn't exist
                string directory = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                // Create default configuration
                var defaultConfig = new EmailConfig
                {
                    Settings = new EmailData
                    {
                        FromEmail = "amvgocatorreport@gmail.com",
                        AppPassword = "zoyr xkfl zxlk dhqy",
                        ToEmails = new List<string> { "vikrant@amvco.com.au" },
                        Subject = "AMV Gocator Report",
                        Body = "Please find attached the Gocator Report for {0} corresponding to Shift {1}."
                    }
                };
                jsonString = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, jsonString);
                Console.WriteLine($"Created default configuration file at {configPath}.");
            }
            jsonString = File.ReadAllText(configPath); // Reuse jsonString
            var config = JsonSerializer.Deserialize<EmailConfig>(jsonString);
            if (config?.Settings != null)
            {
                // Ensure ToEmails is initialized if null
                if (config.Settings.ToEmails == null)
                {
                    config.Settings.ToEmails = new List<string>();
                }
                return config.Settings;
            }
            Console.WriteLine($"Configuration file invalid at {configPath}.");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading or creating configuration: {ex.Message}");
            return null;
        }
    }
}