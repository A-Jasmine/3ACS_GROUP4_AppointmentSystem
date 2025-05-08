using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using System.Net.Mail;
using System.Net;
using Microsoft.Extensions.Configuration;

namespace WebBookingApp.Pages
{
    public class AlumniAppointmentsModel : PageModel
    {
        private readonly ILogger<AlumniAppointmentsModel> _logger;
        private readonly IConfiguration _configuration; // Add a field for IConfiguration
        private readonly string connString; // Connection string will be set in the constructor

        public string FirstName { get; private set; } = "";
        public string LastName { get; private set; } = "";
        public string Role { get; private set; } = "";
        public byte[]? ProfilePicture { get; private set; }

        [BindProperty] public IFormFile? UploadedFile { get; set; }
        public string Message { get; private set; } = "";
        public List<Appointment> PendingAppointments { get; set; } = new();

        public class Appointment
        {
            public int AppointmentID { get; set; }
            public string StudentID { get; set; }
            public string StudentName { get; set; }
            public string Date { get; set; }
            public string Time { get; set; }
            public string Purpose { get; set; }
            public string Mode { get; set; }
            public string YearSection { get; set; }
            public string Notes { get; set; }
            public string Status { get; set; }
            public string StudentEmail { get; set; } // Added for email notification
            public string ApprovalRemarks { get; set; } // Add this line
            public string BookerProfilePicture { get; set; }
        }

        [BindProperty(SupportsGet = true)]
        public string StatusFilter { get; set; } = "All";
        // Inject ILogger and IConfiguration through the constructor
        public AlumniAppointmentsModel(ILogger<AlumniAppointmentsModel> logger, IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            connString = _configuration.GetConnectionString("DefaultConnection");
        } 

        public void OnGet()
        {
            _logger.LogInformation("OnGet() started");

            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail))
            {
                _logger.LogWarning("User not authenticated");
                return;
            }

            using SqlConnection conn = new(connString);
            conn.Open();

            // Try Alumni
            string query = "SELECT alumni_id, first_name, last_name, role FROM dbo.userAlumni WHERE email = @Email";
            string alumniId = "";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    alumniId = reader["alumni_id"].ToString();
                    FirstName = reader["first_name"].ToString();
                    LastName = reader["last_name"].ToString();
                    Role = reader["role"].ToString();
                    _logger.LogInformation($"Retrieved Alumni: {FirstName} {LastName}, Role: {Role}, ID: {alumniId}");
                }
                else
                {
                    _logger.LogWarning("No alumni record found for email: " + userEmail);
                }
            }

            // Try Student if not found in Alumni
            if (string.IsNullOrEmpty(FirstName))
            {
                query = "SELECT first_name, last_name, role FROM dbo.userStudents WHERE email = @Email";
                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    using SqlDataReader reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        FirstName = reader["first_name"].ToString();
                        LastName = reader["last_name"].ToString();
                        Role = reader["role"].ToString();
                        _logger.LogInformation($"Retrieved Student: {FirstName} {LastName}, Role: {Role}");
                    }
                }
            }

            // Get alumni appointments if user is an alumni
            if (!string.IsNullOrEmpty(alumniId))
            {
                query = @"
                SELECT a.AppointmentID, a.StudentID, s.first_name + ' ' + s.last_name AS StudentName, 
                       CONVERT(varchar, a.AppointmentDate, 101) AS Date, 
                       CONVERT(varchar, a.AppointmentTime, 108) AS Time, 
                       a.Purpose, a.Mode, a.Year_Section, a.AdditionalNotes, a.Status,
                       s.email AS StudentEmail,
                       a.ApprovalRemarks,
                       up.Picture AS BookerProfilePicture  -- Add this line
                FROM dbo.StudentAlumniAppointments a
                JOIN dbo.userStudents s ON a.StudentID = s.student_id
                LEFT JOIN dbo.userPictures up ON s.email = up.email  -- Join with userPictures table
                WHERE a.AlumniID = @AlumniID";

                if (!string.IsNullOrEmpty(StatusFilter) && StatusFilter != "All")
                {
                    query += " AND a.Status = @Status";
                }

                _logger.LogInformation("Executing appointment query: " + query);

                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@AlumniID", alumniId);

                    if (!string.IsNullOrEmpty(StatusFilter) && StatusFilter != "All")
                    {
                        cmd.Parameters.AddWithValue("@Status", StatusFilter);
                    }

                    using SqlDataReader reader = cmd.ExecuteReader();
                   while (reader.Read())
{
    byte[]? bookerPicture = reader["BookerProfilePicture"] as byte[];
    string base64BookerPicture = bookerPicture != null
        ? $"data:image/jpeg;base64,{Convert.ToBase64String(bookerPicture)}"
        : "/images/image.png";

    var appt = new Appointment
    {
        AppointmentID = reader.GetInt32(0),
        StudentID = reader["StudentID"].ToString(),
        StudentName = reader["StudentName"].ToString(),
        Date = reader["Date"].ToString(),
        Time = reader["Time"].ToString(),
        Purpose = reader["Purpose"].ToString(),
        Mode = reader["Mode"].ToString(),
        YearSection = reader["Year_Section"].ToString(),
        Status = reader["Status"].ToString(),
        Notes = reader["AdditionalNotes"].ToString(),
        StudentEmail = reader["StudentEmail"].ToString(),
        ApprovalRemarks = reader["ApprovalRemarks"]?.ToString(),
        BookerProfilePicture = base64BookerPicture  // Add this line
    };
    PendingAppointments.Add(appt);
}

                    if (PendingAppointments.Count == 0)
                    {
                        _logger.LogInformation("No appointments found for alumni ID: " + alumniId);
                    }
                }
            }

            // Get profile picture
            query = "SELECT Picture FROM dbo.userPictures WHERE email = @Email";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read() && reader["Picture"] != DBNull.Value)
                {
                    ProfilePicture = (byte[])reader["Picture"];
                    _logger.LogInformation("Profile picture retrieved.");
                }
                else
                {
                    _logger.LogWarning("No profile picture found.");
                }
            }
        }

        public IActionResult OnGetProfilePicture()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) return NotFound();

            using SqlConnection conn = new(connString);
            conn.Open();

            byte[]? profileImage = null;
            string query = "SELECT Picture FROM dbo.userPictures WHERE email = @Email";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read() && reader["Picture"] != DBNull.Value)
                {
                    profileImage = (byte[])reader["Picture"];
                }
            }

            return profileImage != null
                ? File(profileImage, "image/jpeg", "profile.jpg")
                : File("/images/image.png", "image/png");
        }

        public async Task<IActionResult> OnPostUploadPictureAsync()
        {
            if (UploadedFile == null || UploadedFile.Length == 0)
            {
                Message = "Please select a file.";
                _logger.LogWarning("No file uploaded.");
                return Page();
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
            var fileExtension = Path.GetExtension(UploadedFile.FileName).ToLower();
            if (!allowedExtensions.Contains(fileExtension))
            {
                Message = "Only image files (.jpg, .png, .gif) are allowed.";
                _logger.LogWarning("Invalid file format: " + fileExtension);
                return Page();
            }

            try
            {
                byte[] fileData;
                using (var memoryStream = new MemoryStream())
                {
                    await UploadedFile.CopyToAsync(memoryStream);
                    fileData = memoryStream.ToArray();
                }

                string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
                if (string.IsNullOrEmpty(userEmail))
                {
                    Message = "User not authenticated.";
                    return Page();
                }

                using SqlConnection conn = new(connString);
                await conn.OpenAsync();

                string query = @"
                MERGE INTO dbo.userPictures AS target
                USING (SELECT @Email AS Email) AS source
                ON target.email = source.Email
                WHEN MATCHED THEN 
                    UPDATE SET Picture = @ImageData
                WHEN NOT MATCHED THEN 
                    INSERT (email, Picture) VALUES (@Email, @ImageData);";

                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    cmd.Parameters.AddWithValue("@ImageData", fileData);
                    await cmd.ExecuteNonQueryAsync();
                }

                _logger.LogInformation("Profile picture uploaded successfully.");
                return RedirectToPage("/dashboardStudent");
            }
            catch (Exception ex)
            {
                Message = "Error uploading file: " + ex.Message;
                _logger.LogError("Error uploading profile picture: " + ex.Message);
                return Page();
            }
        }
        public async Task<IActionResult> OnPostUpdateAppointment([FromForm] int appointmentId, [FromForm] string status, [FromForm] string remarks = null)
        {
            try
            {
                LoadUserDetails();

                // Get appointment details including student email
                var appointment = await GetAppointmentDetails(appointmentId);
                if (appointment == null)
                {
                    _logger.LogWarning($"Appointment not found: ID {appointmentId}");
                    return new JsonResult(new { success = false, error = "Appointment not found" });
                }

                // Convert time to AM/PM format
                DateTime time;
                string formattedTime = appointment.Time;
                if (DateTime.TryParse(appointment.Time, out time))
                {
                    formattedTime = time.ToString("h:mm tt");
                }

                // First update the appointment status and remarks
                using (SqlConnection conn = new(connString))
                {
                    await conn.OpenAsync();
                    string query = @"UPDATE dbo.StudentAlumniAppointments 
                    SET Status = @Status, 
                        ApprovalRemarks = @Remarks 
                    WHERE AppointmentID = @AppointmentID";

                    using (SqlCommand cmd = new(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Status", status);
                        cmd.Parameters.AddWithValue("@AppointmentID", appointmentId);
                        cmd.Parameters.AddWithValue("@Remarks", string.IsNullOrEmpty(remarks) ? DBNull.Value : remarks);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                _logger.LogInformation($"Appointment {appointmentId} status updated to {status}");

                // Send email notification for status changes
                if (!string.IsNullOrEmpty(appointment.StudentEmail) &&
                    (status.Equals("Approved", StringComparison.OrdinalIgnoreCase) ||
                     status.Equals("Declined", StringComparison.OrdinalIgnoreCase) ||
                     status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)))
                {
                    string subject;
                    string message;

                    if (status.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                    {
                        subject = $"Your Appointment With {FirstName} {LastName} (Alumni) Has Been Approved";
                        message = $@"
        <h3>Dear {appointment.StudentName},</h3>
        <p>I'm pleased to inform you that your appointment with <strong>{FirstName} {LastName} (Alumni)</strong> scheduled for:</p>
        <ul>
            <li><strong>Date:</strong> {appointment.Date}</li>
            <li><strong>Time:</strong> {formattedTime}</li>
            <li><strong>Purpose:</strong> {appointment.Purpose}</li>
            <li><strong>Mode:</strong> {appointment.Mode}</li>
        </ul>
        <p>has been <span style='color:green; font-weight:bold;'>approved</span>.</p>
        {(string.IsNullOrEmpty(remarks) ? "" : $"<p><strong>Approval Remarks:</strong> {remarks}</p>")}
        <p>Please ensure you're prepared for our meeting. If you have any questions or need to share anything beforehand, feel free to reach out.</p>
        <p>Looking forward to our discussion!</p>
        <p>Best regards,<br><strong>{FirstName} {LastName}</strong><br>(Alumni)</p>";
                    }
                    else if (status.Equals("Declined", StringComparison.OrdinalIgnoreCase))
                    {
                        subject = $"Your Appointment Request With {FirstName} {LastName} (Alumni) Has Been Declined";
                        message = $@"
        <h3>Dear {appointment.StudentName},</h3>
        <p>Thank you for booking an appointment with <strong>{FirstName} {LastName} (Alumni)</strong> scheduled for has been <span style='color:red; font-weight:bold;'>declined</span>.</p>
        <ul>
            <li><strong>Date:</strong> {appointment.Date}</li>
            <li><strong>Time:</strong> {formattedTime}</li>
            <li><strong>Purpose:</strong> {appointment.Purpose}</li>
            <li><strong>Mode:</strong> {appointment.Mode}</li>
        </ul>
     
        {(string.IsNullOrEmpty(remarks) ? "" : $"<p><strong>Decline Remarks:</strong> {remarks}</p>")}
        <p>If you'd like to reschedule or discuss alternative options, please don't hesitate to contact me. I'm happy to find a more suitable time for both of us.</p>
        <p>We understand that this may be disappointing, and we strongly encourage you to reach out if you wish to reschedule or discuss alternative options. We remain available to arrange a more suitable time for the meeting.</p>
        <p>Sincerely,<br><strong>{FirstName} {LastName}</strong><br>(Alumni)</p>";
                    }
                    else // Cancelled
                    {
                        subject = $"Your Appointment With {FirstName} {LastName} (Alumni) Has Been Cancelled";
                        message = $@"
        <h3>Dear {appointment.StudentName},</h3>
        <p>We regret to inform you that your approved appointment with <strong>{FirstName} {LastName} (Alumni)</strong> has been <span style='color:red; font-weight:bold;'>cancelled</span>.</p>
        <p><strong>Original Appointment Details:</strong></p>
        <ul>
            <li><strong>Date:</strong> {appointment.Date}</li>
            <li><strong>Time:</strong> {formattedTime}</li>
            <li><strong>Purpose:</strong> {appointment.Purpose}</li>
            <li><strong>Mode:</strong> {appointment.Mode}</li>
        </ul>
        {(string.IsNullOrEmpty(remarks) ? "" : $"<p><strong>Cancellation Reason:</strong> {remarks}</p>")}
        <p>We sincerely apologize for any inconvenience this may cause. You may want to:</p>
        <ul>
            <li>Request a new appointment at a different time</li>
            <li>Contact me directly for alternative arrangements</li>
        </ul>
        <p>Thank you for your understanding.</p>
        <p>Sincerely,<br><strong>{FirstName} {LastName}</strong><br>(Alumni)</p>";
                    }

                    await SendEmailAsync(appointment.StudentEmail, subject, message);
                    _logger.LogInformation($"Email notification sent for appointment {appointmentId} with status {status}");
                }

                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating appointment: {ex.Message}");
                _logger.LogError($"Stack Trace: {ex.StackTrace}");
                return new JsonResult(new { success = false, error = ex.Message });
            }
        }


        private async Task<Appointment> GetAppointmentDetails(int appointmentId)
        {
            using (SqlConnection conn = new(connString))
            {
                await conn.OpenAsync();
                string query = @"
            SELECT 
                a.AppointmentID, 
                a.StudentID, 
                s.first_name + ' ' + s.last_name AS StudentName,
                CONVERT(varchar, a.AppointmentDate, 101) AS Date,
                CONVERT(varchar, a.AppointmentTime, 108) AS Time,
                a.Purpose, 
                a.Mode, 
                a.Year_Section, 
                a.AdditionalNotes, 
                a.Status,
                s.email AS StudentEmail,
                a.ApprovalRemarks,
                up.Picture AS BookerProfilePicture
            FROM dbo.StudentAlumniAppointments a
            JOIN dbo.userStudents s ON a.StudentID = s.student_id
            LEFT JOIN dbo.userPictures up ON s.email = up.email
            WHERE a.AppointmentID = @AppointmentID";

                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@AppointmentID", appointmentId);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            byte[]? bookerPicture = reader["BookerProfilePicture"] as byte[];
                            string base64BookerPicture = bookerPicture != null
                                ? $"data:image/jpeg;base64,{Convert.ToBase64String(bookerPicture)}"
                                : "/images/image.png";

                            return new Appointment
                            {
                                AppointmentID = reader.GetInt32(0),
                                StudentID = reader["StudentID"].ToString(),
                                StudentName = reader["StudentName"].ToString(),
                                Date = reader["Date"].ToString(),
                                Time = reader["Time"].ToString(),
                                Purpose = reader["Purpose"].ToString(),
                                Mode = reader["Mode"].ToString(),
                                YearSection = reader["Year_Section"].ToString(),
                                Status = reader["Status"].ToString(),
                                Notes = reader["AdditionalNotes"].ToString(),
                                StudentEmail = reader["StudentEmail"].ToString(),
                                ApprovalRemarks = reader["ApprovalRemarks"]?.ToString(),
                                BookerProfilePicture = base64BookerPicture
                            };
                        }
                    }
                }
            }
            return null;
        }


        private async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            try
            {
                var emailConfig = _configuration.GetSection("EmailConfig");
                var smtpServer = emailConfig["SmtpServer"];
                var smtpPort = int.Parse(emailConfig["smtpPort"]);
                var username = emailConfig["Username"];
                var password = emailConfig["Password"];
                var fromEmail = emailConfig["FromEmail"];
                var fromDisplay = emailConfig["FromName"] ?? "Alumni Booking";

                using var client = new SmtpClient(smtpServer, smtpPort)
                {
                    EnableSsl = true,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(username, password),
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(fromEmail, fromDisplay),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

                mailMessage.To.Add(toEmail);

                await client.SendMailAsync(mailMessage);
                _logger.LogInformation($"Email sent to {toEmail} with subject: {subject}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending email: {ex.Message}");
                throw;
            }
        }

        private void LoadUserDetails()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) return;

            using SqlConnection conn = new(connString);
            conn.Open();

            string query = "SELECT first_name, last_name, role FROM dbo.userAlumni WHERE email = @Email";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    FirstName = reader["first_name"].ToString();
                    LastName = reader["last_name"].ToString();
                    Role = reader["role"].ToString();
                    return;
                }
            }

            // Check students if not found in alumni
            query = "SELECT first_name, last_name, role FROM dbo.userStudents WHERE email = @Email";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    FirstName = reader["first_name"].ToString();
                    LastName = reader["last_name"].ToString();
                    Role = reader["role"].ToString();
                }
            }
        }


        public async Task<IActionResult> OnPostLogout()
        {
            await HttpContext.SignOutAsync();
            return RedirectToPage("/Login");
        }
    }
}