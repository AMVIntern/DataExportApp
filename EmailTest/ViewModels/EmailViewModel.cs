using System;
using System.Net.Mail;
using System.Net;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

public class EmailViewModel
{
    private readonly EmailData _emailData;

    public EmailViewModel(EmailData emailData)
    {
        _emailData = emailData ?? throw new ArgumentNullException(nameof(emailData));
    }

    public void GenerateAndSendReport()
    {
        try
        {
            string topFolder = @"C:\AMV\Gocator\Top";
            string bottomFolder = @"C:\AMV\Gocator\Bottom";
            string combinedFolder = @"C:\AMV\Gocator\Combined";

            // Create combined folder if it doesn't exist
            Directory.CreateDirectory(combinedFolder);

            // Find most recent CSV file containing "values" in Top folder
            string topFile = Directory.GetFiles(topFolder, "*.csv")
                .Where(f => Path.GetFileName(f).ToLower().Contains("values"))
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .FirstOrDefault();

            if (topFile == null)
            {
                Console.WriteLine("No CSV file containing 'values' found in Top folder.");
                return;
            }

            // Find most recent CSV file containing "values" in Bottom folder
            string bottomFile = Directory.GetFiles(bottomFolder, "*.csv")
                .Where(f => Path.GetFileName(f).ToLower().Contains("values"))
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .FirstOrDefault();

            if (bottomFile == null)
            {
                Console.WriteLine("No CSV file containing 'values' found in Bottom folder.");
                return;
            }

            // Read Top CSV
            string[] topLines = File.ReadAllLines(topFile);
            if (topLines.Length < 2) // Need at least header + 1 data row
            {
                Console.WriteLine("Top CSV file has insufficient data rows.");
                return;
            }
            string[] topHeader = topLines[0].Split(',');
            // Reorder topHeader to move Shift to third position
            string[] reorderedTopHeader = new[] { topHeader[1], topHeader[2], topHeader[0], topHeader[3], topHeader[4], topHeader[5] };
            List<TopRow> topRows = new List<TopRow>();
            for (int k = 1; k < topLines.Length; k++)
            {
                string[] vals = topLines[k].Split(',');
                if (vals.Length != topHeader.Length) continue;
                var row = new TopRow
                {
                    TopDate = vals[1],
                    TopTimestamp = vals[2],
                    Shift = vals[0], // Read Shift from first column
                    TopBoardCount = double.Parse(vals[3], CultureInfo.InvariantCulture),
                    TopSquarenessDifference = double.Parse(vals[4], CultureInfo.InvariantCulture),
                    TopOverallPass = double.Parse(vals[5], CultureInfo.InvariantCulture)
                };
                try
                {
                    DateTime date = DateTime.ParseExact(row.TopDate, "dd-MMM-yyyy", CultureInfo.InvariantCulture);
                    TimeSpan time = TimeSpan.ParseExact(row.TopTimestamp, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
                    row.FullTimestamp = date + time;
                }
                catch
                {
                    Console.WriteLine($"Invalid date/timestamp in Top row {k}");
                    continue;
                }
                topRows.Add(row);
            }

            // Read Bottom CSV
            string[] bottomLines = File.ReadAllLines(bottomFile);
            if (bottomLines.Length < 1) return;
            string[] bottomHeader = bottomLines[0].Split(',').Skip(2).ToArray(); // Skip Bot:Date and Bot:Timestamp
            List<BottomRow> bottomRows = new List<BottomRow>();
            for (int k = 1; k < bottomLines.Length; k++)
            {
                string[] vals = bottomLines[k].Split(',');
                if (vals.Length != bottomHeader.Length + 2) continue; // Adjust for skipped columns
                var row = new BottomRow
                {
                    BotDate = vals[0],
                    BotTimestamp = vals[1],
                    BotBoardCount = double.Parse(vals[2], CultureInfo.InvariantCulture),
                    BotBLB1_B1PushBack = double.Parse(vals[3], CultureInfo.InvariantCulture),
                    BotBLB1_B3PushBack = double.Parse(vals[4], CultureInfo.InvariantCulture),
                    BotBLB1MaxPushBackDist = double.Parse(vals[5], CultureInfo.InvariantCulture),
                    BotBLB1Width = double.Parse(vals[6], CultureInfo.InvariantCulture),
                    BotTG1MinTunnelGapDist = double.Parse(vals[7], CultureInfo.InvariantCulture),
                    BotBIB2TopOffsetDist = double.Parse(vals[8], CultureInfo.InvariantCulture),
                    BotBIB2BottomOffsetDist = double.Parse(vals[9], CultureInfo.InvariantCulture),
                    BotTG2MinTunnelGapDist = double.Parse(vals[10], CultureInfo.InvariantCulture),
                    BotBLB2Width = double.Parse(vals[11], CultureInfo.InvariantCulture),
                    BotBLB2_B1PushBack = double.Parse(vals[12], CultureInfo.InvariantCulture),
                    BotBLB2_B3PushBack = double.Parse(vals[13], CultureInfo.InvariantCulture),
                    BotBLB2MaxPushBackDist = double.Parse(vals[14], CultureInfo.InvariantCulture),
                    BotOverall_Result = double.Parse(vals[15], CultureInfo.InvariantCulture)
                };
                try
                {
                    DateTime date = DateTime.ParseExact(row.BotDate, "dd-MMM-yyyy", CultureInfo.InvariantCulture);
                    TimeSpan time = TimeSpan.ParseExact(row.BotTimestamp, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
                    row.FullTimestamp = date + time;
                }
                catch
                {
                    Console.WriteLine($"Invalid date/timestamp in Bottom row {k}");
                    continue;
                }
                bottomRows.Add(row);
            }

            // Sort both lists by FullTimestamp
            topRows = topRows.OrderBy(r => r.FullTimestamp).ToList();
            bottomRows = bottomRows.OrderBy(r => r.FullTimestamp).ToList();

            // Combine headers, including Assured_Result
            string[] combinedHeader = reorderedTopHeader.Concat(bottomHeader).Concat(new[] { "Assured_Result" }).ToArray();

            // Prepare combined data
            List<string[]> combinedData = new List<string[]>();
            combinedData.Add(combinedHeader);

            // Match only rows within 1.5 seconds, ignoring unmatched rows, and calculate Assured_Result
            int i = 0, j = 0;
            while (i < topRows.Count && j < bottomRows.Count)
            {
                double diff = Math.Abs((topRows[i].FullTimestamp.Value - bottomRows[j].FullTimestamp.Value).TotalSeconds);
                if (diff < 1.5) // Match within 1.5 seconds
                {
                    // Calculate Assured_Result as TopOverallPass * BotOverall_Result
                    double assuredResult = topRows[i].TopOverallPass * bottomRows[j].BotOverall_Result;
                    string[] topData = topRows[i].ToStringArray();
                    string[] bottomData = bottomRows[j].ToStringArray();
                    string[] row = topData.Concat(bottomData).Concat(new[] { assuredResult.ToString(CultureInfo.InvariantCulture) }).ToArray();
                    combinedData.Add(row);
                    i++;
                    j++;
                }
                else if (topRows[i].FullTimestamp < bottomRows[j].FullTimestamp)
                {
                    i++; // Skip unmatched Top row
                }
                else
                {
                    j++; // Skip unmatched Bottom row
                }
            }

            // Save combined CSV with CSV-derived naming convention
            string dateTimeString = DateTime.Now.ToString("dd-MM-yyyy_HH-mm-ss"); // Use current time for file name uniqueness
            string combinedFile = Path.Combine(combinedFolder, $"Gocator_Report_Shift_{combinedData[1][2]}_{combinedData[1][0]}.csv"); using (StreamWriter writer = new StreamWriter(combinedFile))
            {
                foreach (var row in combinedData)
                {
                    writer.WriteLine(string.Join(",", row));
                }
            }
            Console.WriteLine($"Combined CSV saved to: {combinedFile}");

            // Read shift and date from the first data row of the combined file
            if (combinedData.Count < 2)
            {
                Console.WriteLine("Combined CSV has no data rows.");
                return;
            }
            string combinedShift = combinedData[1][2]; // Shift is in the third column (index 2) after reordering
            string combinedDate = combinedData[1][0];  // Date is in the first column (index 0)
            string combinedTimestamp = combinedData[1][1]; // Timestamp is in the second column (index 1)
            DateTime combinedReportDate;
            try
            {
                combinedReportDate = DateTime.ParseExact(combinedDate, "dd-MMM-yyyy", CultureInfo.InvariantCulture)
                    + TimeSpan.ParseExact(combinedTimestamp, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
            }
            catch
            {
                Console.WriteLine("Invalid date or timestamp in first row of combined CSV.");
                return;
            }

            // Set attachment path and update email content with combined file data
            _emailData.AttachmentPath = combinedFile;
            _emailData.Subject = string.Format(_emailData.Subject, combinedDate, combinedShift);
            _emailData.Body = $"Please find attached the Gocator Report for {combinedDate} corresponding to Shift {combinedShift}.";
            SendEmail();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating report: {ex.Message}");
        }
    }

    public bool SendEmail()
    {
        try
        {
            // Create the mail message
            using (MailMessage mail = new MailMessage())
            {
                mail.From = new MailAddress(_emailData.FromEmail); // Corrected to FromEmail
                // Add multiple recipients from ToEmails list
                if (_emailData.ToEmails != null)
                {
                    foreach (var toEmail in _emailData.ToEmails)
                    {
                        mail.To.Add(toEmail);
                    }
                }
                else
                {
                    Console.WriteLine("No recipients specified.");
                    return false;
                }
                mail.Subject = _emailData.Subject;
                mail.Body = _emailData.Body;
                mail.IsBodyHtml = false;

                // Attach the file if it exists
                if (!string.IsNullOrEmpty(_emailData.AttachmentPath) && File.Exists(_emailData.AttachmentPath))
                {
                    Attachment attachment = new Attachment(_emailData.AttachmentPath);
                    mail.Attachments.Add(attachment);
                }
                else if (!string.IsNullOrEmpty(_emailData.AttachmentPath))
                {
                    Console.WriteLine("Attachment file not found.");
                    return false;
                }

                // Configure the SMTP client
                using (SmtpClient smtpClient = new SmtpClient("smtp.gmail.com", 587))
                {
                    smtpClient.EnableSsl = true;
                    smtpClient.UseDefaultCredentials = false;
                    smtpClient.Credentials = new NetworkCredential(_emailData.FromEmail, _emailData.AppPassword);
                    smtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;

                    // Send the email
                    smtpClient.Send(mail);
                    Console.WriteLine("Email sent successfully!");
                    return true;
                }
            }
        }
        catch (SmtpException ex)
        {
            Console.WriteLine($"SMTP Error: {ex.Message}");
            Console.WriteLine($"Status Code: {ex.StatusCode}");
            if (ex.InnerException != null)
                Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"General Error: {ex.Message}");
            return false;
        }
    }

    private static string GetCurrentShift(DateTime now)
    {
        int hour = now.Hour;
        if (hour >= 6 && hour < 14) return "1"; // 6 AM to 2 PM
        else if (hour >= 14 && hour < 22) return "2"; // 2 PM to 10 PM
        else return "3"; // 10 PM to 6 AM
    }
}

public class TopRow
{
    public string TopDate { get; set; }
    public string TopTimestamp { get; set; }
    public string Shift { get; set; }
    public double TopBoardCount { get; set; }
    public double TopSquarenessDifference { get; set; }
    public double TopOverallPass { get; set; }
    public DateTime? FullTimestamp { get; set; }

    public string[] ToStringArray()
    {
        return new[]
        {
            TopDate,
            TopTimestamp,
            Shift,
            TopBoardCount.ToString(CultureInfo.InvariantCulture),
            TopSquarenessDifference.ToString(CultureInfo.InvariantCulture),
            TopOverallPass.ToString(CultureInfo.InvariantCulture)
        };
    }
}

public class BottomRow
{
    public string BotDate { get; set; }
    public string BotTimestamp { get; set; }
    public double BotBoardCount { get; set; }
    public double BotBLB1_B1PushBack { get; set; }
    public double BotBLB1_B3PushBack { get; set; }
    public double BotBLB1MaxPushBackDist { get; set; }
    public double BotBLB1Width { get; set; }
    public double BotTG1MinTunnelGapDist { get; set; }
    public double BotBIB2TopOffsetDist { get; set; }
    public double BotBIB2BottomOffsetDist { get; set; }
    public double BotTG2MinTunnelGapDist { get; set; }
    public double BotBLB2Width { get; set; }
    public double BotBLB2_B1PushBack { get; set; }
    public double BotBLB2_B3PushBack { get; set; }
    public double BotBLB2MaxPushBackDist { get; set; }
    public double BotOverall_Result { get; set; }
    public DateTime? FullTimestamp { get; set; }

    public string[] ToStringArray()
    {
        return new[]
        {
            BotBoardCount.ToString(CultureInfo.InvariantCulture),
            BotBLB1_B1PushBack.ToString(CultureInfo.InvariantCulture),
            BotBLB1_B3PushBack.ToString(CultureInfo.InvariantCulture),
            BotBLB1MaxPushBackDist.ToString(CultureInfo.InvariantCulture),
            BotBLB1Width.ToString(CultureInfo.InvariantCulture),
            BotTG1MinTunnelGapDist.ToString(CultureInfo.InvariantCulture),
            BotBIB2TopOffsetDist.ToString(CultureInfo.InvariantCulture),
            BotBIB2BottomOffsetDist.ToString(CultureInfo.InvariantCulture),
            BotTG2MinTunnelGapDist.ToString(CultureInfo.InvariantCulture),
            BotBLB2Width.ToString(CultureInfo.InvariantCulture),
            BotBLB2_B1PushBack.ToString(CultureInfo.InvariantCulture),
            BotBLB2_B3PushBack.ToString(CultureInfo.InvariantCulture),
            BotBLB2MaxPushBackDist.ToString(CultureInfo.InvariantCulture),
            BotOverall_Result.ToString(CultureInfo.InvariantCulture)
        };
    }
}