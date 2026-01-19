using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

public class ReportState
{
    [JsonPropertyName("reports")]
    public List<ReportEntry> Reports { get; set; } = new List<ReportEntry>();
}

public class ReportEntry
{
    [JsonPropertyName("scheduledTime")]
    public string ScheduledTime { get; set; }

    [JsonPropertyName("fileName")]
    public string FileName { get; set; }

    [JsonPropertyName("sendMail")]
    public bool SendMail { get; set; }

    [JsonIgnore]
    public DateTime ScheduledDateTime
    {
        get
        {
            if (DateTime.TryParse(ScheduledTime, out DateTime result))
                return result;
            return DateTime.MinValue;
        }
        set
        {
            ScheduledTime = value.ToString("yyyy-MM-ddTHH:mm:ss");
        }
    }
}
