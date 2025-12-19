using System;
using System.IO;
using System.Threading;
using System.Text.Json;

class Program
{
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
        var viewModel = new EmailViewModel(emailData);

        // Run in an infinite loop to wait for scheduled times
        while (true)
        {
            var now = DateTime.Now;

            // Define custom scheduled times (in 24-hour format)
            TimeSpan[] scheduledTimeSpans = new TimeSpan[]
            {
                new TimeSpan(6, 8, 0), // 6:00 AM
                new TimeSpan(14, 0, 0), // 2:00 PM
                new TimeSpan(22, 0, 0)  // 10:00 PM
            };

            // Find the next scheduled time
            DateTime nextScheduledTime = GetNextScheduledTime(now, scheduledTimeSpans);

            // Check if the next scheduled time falls on a Saturday or Sunday
            while (nextScheduledTime.DayOfWeek == DayOfWeek.Saturday || nextScheduledTime.DayOfWeek == DayOfWeek.Sunday)
            {
                // Move to the next Monday by adding days (6 if Sunday, 1 if Saturday)
                int daysToAdd = nextScheduledTime.DayOfWeek == DayOfWeek.Sunday ? 1 : (8 - (int)nextScheduledTime.DayOfWeek);
                nextScheduledTime = nextScheduledTime.AddDays(daysToAdd);
                //nextScheduledTime = GetNextScheduledTime(nextScheduledTime, scheduledTimeSpans);
            }

            // Calculate initial delay to next scheduled time
            TimeSpan delay = nextScheduledTime - now;
            Console.WriteLine($"Next report scheduled at {nextScheduledTime}. Waiting for {delay.TotalMinutes:F2} minutes...");

            // Update counter every 10 seconds until the scheduled time
            while (now < nextScheduledTime)
            {
                TimeSpan remaining = nextScheduledTime - DateTime.Now;
                Console.Write($"\rRemaining time: {remaining.TotalMinutes:F2} minutes "); // \r for overwrite
                Thread.Sleep(10000); // Update every 10 seconds
                now = DateTime.Now;
            }
            Console.WriteLine(); // New line after countdown

            // Generate combined CSV and send email (shift and date from CSV)
            viewModel.GenerateAndSendReport();
        }
    }

    private static DateTime GetNextScheduledTime(DateTime now, TimeSpan[] scheduledTimeSpans)
    {
        // Sort the scheduled times
        var sortedTimes = scheduledTimeSpans.OrderBy(t => t).ToArray();
        foreach (var timeSpan in sortedTimes)
        {
            DateTime candidate = now.Date + timeSpan;
            if (candidate > now)
            {
                return candidate;
            }
        }
        // If all times today have passed, return the first time tomorrow
        return now.Date.AddDays(1) + sortedTimes[0];
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