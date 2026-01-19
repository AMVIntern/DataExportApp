using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

public class ReportStateService
{
    private readonly string _jsonFilePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public ReportStateService(string jsonFilePath)
    {
        _jsonFilePath = jsonFilePath ?? throw new ArgumentNullException(nameof(jsonFilePath));
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public ReportState LoadReportState()
    {
        try
        {
            // Create directory if it doesn't exist
            string directory = Path.GetDirectoryName(_jsonFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(_jsonFilePath))
            {
                // Create empty state file
                var emptyState = new ReportState();
                SaveReportState(emptyState);
                return emptyState;
            }

            string jsonString = File.ReadAllText(_jsonFilePath);
            var state = JsonSerializer.Deserialize<ReportState>(jsonString, _jsonOptions);
            return state ?? new ReportState();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading report state: {ex.Message}");
            return new ReportState();
        }
    }

    public void SaveReportState(ReportState state)
    {
        try
        {
            // Create directory if it doesn't exist
            string directory = Path.GetDirectoryName(_jsonFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string jsonString = JsonSerializer.Serialize(state, _jsonOptions);
            File.WriteAllText(_jsonFilePath, jsonString);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving report state: {ex.Message}");
        }
    }

    public List<ReportEntry> GetPendingReports(DateTime currentTime)
    {
        var state = LoadReportState();
        return state.Reports
            .Where(r => !r.SendMail && r.ScheduledDateTime <= currentTime)
            .ToList();
    }

    public void MarkReportAsSent(string fileName, DateTime scheduledTime)
    {
        var state = LoadReportState();
        var entry = state.Reports.FirstOrDefault(r => 
            r.FileName == fileName && 
            r.ScheduledDateTime == scheduledTime);

        if (entry != null)
        {
            entry.SendMail = true;
            SaveReportState(state);
        }
    }

    public void EnsureReportEntry(DateTime scheduledTime, string fileName)
    {
        var state = LoadReportState();
        string scheduledTimeStr = scheduledTime.ToString("yyyy-MM-ddTHH:mm:ss");

        var existingEntry = state.Reports.FirstOrDefault(r => 
            r.ScheduledTime == scheduledTimeStr && 
            r.FileName == fileName);

        if (existingEntry == null)
        {
            state.Reports.Add(new ReportEntry
            {
                ScheduledTime = scheduledTimeStr,
                FileName = fileName,
                SendMail = false
            });
            SaveReportState(state);
        }
    }

    public void UpdateReportEntryAfterSend(DateTime scheduledTime, string fileName, bool success)
    {
        var state = LoadReportState();
        string scheduledTimeStr = scheduledTime.ToString("yyyy-MM-ddTHH:mm:ss");

        var entry = state.Reports.FirstOrDefault(r => 
            r.ScheduledTime == scheduledTimeStr && 
            r.FileName == fileName);

        if (entry != null)
        {
            entry.SendMail = success;
            SaveReportState(state);
        }
    }
}
