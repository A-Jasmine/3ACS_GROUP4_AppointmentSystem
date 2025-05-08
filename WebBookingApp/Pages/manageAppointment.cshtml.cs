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
using Microsoft.Extensions.Configuration;
using System.Net.Mail;
using System.Net;
using static WebBookingApp.Pages.makeAppointmentModel;

namespace WebBookingApp.Pages
{
    public class manageAppointmentModel : PageModel
    {
        private readonly ILogger<manageAppointmentModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _connString;


        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public byte[]? ProfilePicture { get; set; }
        public string Role { get; set; } = "";
        public string Message { get; set; } = string.Empty;

        [BindProperty(SupportsGet = true)]
        public string StatusFilter { get; set; } = "All";

        [BindProperty] public IFormFile? UploadedFile { get; set; }

        // Appointment management properties
        public List<Appointment> PendingAppointments { get; set; } = new();

        public class Appointment
        {
            public int Id { get; set; }
            public string BookedBy { get; set; } = "";
            public string BookerId { get; set; } = "";
            public string BookerName { get; set; } = "";
            public DateTime AppointmentDate { get; set; }
            public TimeSpan AppointmentTime { get; set; }
            public string Purpose { get; set; } = "";
            public string Mode { get; set; } = "";
            public string AdditionalNotes { get; set; } = "";
            public string YearSection { get; set; } = "";
            public string BookerEmail { get; set; } = "";
            public string ApprovalRemarks { get; set; } = ""; // Used only for email
            public string Status { get; set; } = "";
            public string BookerProfilePicture { get; set; } = "";

            public string FormattedAppointmentTime => DateTime.Today.Add(AppointmentTime).ToString("h:mm tt");
        }
        public manageAppointmentModel(ILogger<manageAppointmentModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _connString = _configuration.GetConnectionString("DefaultConnection");
        }


        public async Task OnGetAsync()
        {
            _logger.LogInformation("OnGet() started");
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                _logger.LogWarning("User not authenticated");
                return;
            }

            // Define tasks for fetching data
            var fetchNameAndRoleTask = Task.Run(async () =>
            {
                try
                {
                    using SqlConnection conn = new(_connString);
                    await conn.OpenAsync();

                    string query = "SELECT first_name, last_name, role FROM dbo.userCICT WHERE email = @Email";
                    using SqlCommand cmd = new(query, conn);
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    using SqlDataReader reader = await cmd.ExecuteReaderAsync();
                    if (reader.Read())
                    {
                        FirstName = reader["first_name"].ToString();
                        LastName = reader["last_name"].ToString();
                        Role = reader["role"].ToString();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error fetching name and role: {ex.Message}");
                }
            });

            var fetchProfilePictureTask = Task.Run(async () =>
            {
                try
                {
                    using SqlConnection conn = new(_connString);
                    await conn.OpenAsync();

                    string query = "SELECT Picture FROM dbo.userPictures WHERE email = @Email";
                    using SqlCommand cmd = new(query, conn);
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    using SqlDataReader reader = await cmd.ExecuteReaderAsync();
                    if (reader.Read() && reader["Picture"] != DBNull.Value)
                    {
                        ProfilePicture = (byte[])reader["Picture"];
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error fetching profile picture: {ex.Message}");
                }
            });

            var fetchPendingAppointmentsTask = Task.Run(async () =>
            {
                try
                {
                    using SqlConnection conn = new(_connString);
                    await conn.OpenAsync();

                    // Get professor ID
                    int professorId;
                    string query = "SELECT professor_id FROM dbo.userCICT WHERE email = @Email";
                    using (SqlCommand cmd = new(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Email", userEmail);
                        professorId = (int)await cmd.ExecuteScalarAsync();
                    }

                    query = @"
SELECT 
    a.appointment_id,
    CASE 
        WHEN a.student_id IS NOT NULL THEN 'Student'
        ELSE 'Alumni'
    END AS booked_by,
    COALESCE(a.student_id, a.alumni_id) AS booker_id,
    COALESCE(s.first_name + ' ' + s.last_name, al.first_name + ' ' + al.last_name) AS booker_name,
    a.appointment_date,
    a.appointment_time,
    a.purpose,
    a.Mode,
    a.additionalnotes,
    a.Year_Section,
    CAST(a.status AS NVARCHAR(50)) AS status,
    COALESCE(s.email, al.email) AS booker_email,
    a.ApprovalRemarks,
    CASE 
        WHEN EXISTS (SELECT 1 FROM dbo.userPictures WHERE email = COALESCE(s.email, al.email)) 
        THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) 
    END AS has_profile_picture
FROM dbo.Appointments a
LEFT JOIN dbo.userStudents s ON a.student_id = s.student_id
LEFT JOIN dbo.userAlumni al ON a.alumni_id = al.alumni_id
WHERE a.professor_id = @ProfessorId";

                    // Add status filter if not "All"
                    if (!string.IsNullOrEmpty(StatusFilter) && StatusFilter != "All")
                    {
                        query += " AND a.status = @Status";
                    }

                    query += " ORDER BY a.appointment_date DESC, a.appointment_time DESC";

                    using (SqlCommand cmd = new(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@ProfessorId", professorId);

                        // Add status parameter only when needed
                        if (!string.IsNullOrEmpty(StatusFilter) && StatusFilter != "All")
                        {
                            cmd.Parameters.AddWithValue("@Status", StatusFilter);
                        }

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (reader.Read())
                            {
                                var appointment = new Appointment
                                {
                                    Id = reader.GetInt32(0),
                                    BookedBy = reader.GetString(1),
                                    BookerId = reader.GetString(2),
                                    BookerName = reader.GetString(3),
                                    AppointmentDate = reader.GetDateTime(4),
                                    AppointmentTime = reader.GetTimeSpan(5),
                                    Purpose = reader.GetString(6),
                                    Mode = reader.GetString(7),
                                    AdditionalNotes = reader.IsDBNull(8) ? "" : reader.GetString(8),
                                    YearSection = reader.IsDBNull(9) ? "" : reader.GetString(9),
                                    Status = reader.GetString(10),
                                    BookerEmail = reader.IsDBNull(11) ? "" : reader.GetString(11),
                                    ApprovalRemarks = reader.IsDBNull(12) ? "" : reader.GetString(12)
                                };

                                // Get profile picture
                                if (!reader.IsDBNull(13) && reader.GetBoolean(13)) // has_profile_picture
                                {
                                    string bookerEmail = reader.IsDBNull(11) ? "" : reader.GetString(11);
                                    if (!string.IsNullOrEmpty(bookerEmail))
                                    {
                                        appointment.BookerProfilePicture = await GetProfilePictureBase64(bookerEmail);
                                    }
                                }
                                else
                                {
                                    appointment.BookerProfilePicture = "/images/image.png";
                                }

                                PendingAppointments.Add(appointment);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error fetching pending appointments: {ex.Message}");
                }
            });
            // Run tasks in parallel
            await Task.WhenAll(fetchNameAndRoleTask, fetchProfilePictureTask, fetchPendingAppointmentsTask);

            _logger.LogInformation("Data fetched successfully.");
        }

        private async Task<string> GetProfilePictureBase64(string email)
        {
            try
            {
                using SqlConnection conn = new(_connString);
                await conn.OpenAsync();

                string query = "SELECT Picture FROM dbo.userPictures WHERE email = @Email";
                using SqlCommand cmd = new(query, conn);
                cmd.Parameters.AddWithValue("@Email", email);

                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    byte[] imageData = (byte[])result;
                    return $"data:image/jpeg;base64,{Convert.ToBase64String(imageData)}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching profile picture for {email}: {ex.Message}");
            }
            return "/images/image.png";
        }

        public IActionResult OnGetProfilePicture()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) return NotFound();

            byte[]? profileImage = null;

            using SqlConnection conn = new(_connString);
            conn.Open();

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

            if (profileImage != null)
            {
                Response.Headers["Content-Disposition"] = "inline; filename=profile.jpg";
                return File(profileImage, "image/jpeg");
            }

            return File("/images/image.png", "image/png");
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

                using SqlConnection conn = new(_connString);
                await conn.OpenAsync();

                bool isProfessor = false;
                string checkProfessorQuery = "SELECT COUNT(*) FROM dbo.userCICT WHERE email = @Email";
                using (SqlCommand checkCmd = new(checkProfessorQuery, conn))
                {
                    checkCmd.Parameters.AddWithValue("@Email", userEmail);
                    isProfessor = (int)await checkCmd.ExecuteScalarAsync() > 0;
                }

                string query = @"
                MERGE INTO dbo.userPictures AS target
                USING (SELECT @Email AS Email) AS source
                ON target.email = source.Email
                WHEN MATCHED THEN 
                    UPDATE SET Picture = @ImageData, isProfessor = @IsProfessor
                WHEN NOT MATCHED THEN 
                    INSERT (email, Picture, isProfessor) VALUES (@Email, @ImageData, @IsProfessor);";

                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    cmd.Parameters.AddWithValue("@ImageData", fileData);
                    cmd.Parameters.AddWithValue("@IsProfessor", isProfessor);
                    await cmd.ExecuteNonQueryAsync();
                }

                _logger.LogInformation("Profile picture uploaded successfully.");
                return RedirectToPage("/dashboardProfessor");
            }
            catch (Exception ex)
            {
                Message = "Error uploading file: " + ex.Message;
                _logger.LogError("Error uploading profile picture: " + ex.Message);
                return Page();
            }
        }

        public async Task<IActionResult> OnPostApprove(int id, string remarks)
        {
            try
            {
                var result = await UpdateAppointmentStatus(id, "Approved", remarks);
                return RedirectToPage(new { action = "approve", success = "true" });
            }
            catch
            {
                return RedirectToPage(new { action = "approve", success = "false" });
            }
        }

        public async Task<IActionResult> OnPostDecline(int id, string remarks)
        {
            try
            {
                var result = await UpdateAppointmentStatus(id, "Declined", remarks);
                return RedirectToPage(new { action = "decline", success = "true" });
            }
            catch
            {
                return RedirectToPage(new { action = "decline", success = "false" });
            }
        }
        public async Task<IActionResult> OnPostCancel(int id, string remarks)
        {
            try
            {
                // First check if appointment exists and is approved
                using SqlConnection conn = new(_connString);
                await conn.OpenAsync();

                string currentStatus;
                string query = "SELECT status FROM dbo.Appointments WHERE appointment_id = @Id";
                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    currentStatus = (string)await cmd.ExecuteScalarAsync();
                }

                if (currentStatus != "Approved")
                {
                    Message = "Only approved appointments can be cancelled";
                    return Page();
                }

                var result = await UpdateAppointmentStatus(id, "Cancelled", remarks);
                return RedirectToPage(new { action = "cancel", success = "true" });
            }
            catch
            {
                return RedirectToPage(new { action = "cancel", success = "false" });
            }
        }

        private async Task<IActionResult> UpdateAppointmentStatus(int id, string newStatus, string remarks)
        {
            try
            {
                using SqlConnection conn = new(_connString);
                await conn.OpenAsync();

                // Get current status before updating
                string currentStatus;
                string query = "SELECT status FROM dbo.Appointments WHERE appointment_id = @Id";
                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    currentStatus = (string)await cmd.ExecuteScalarAsync();
                }

                // Get full appointment details
                (Appointment appointment, string professorName) = await GetAppointmentDetails(conn, id);

                if (appointment == null)
                {
                    Message = "Appointment not found";
                    return Page();
                }

                // Update the status
                query = "UPDATE dbo.Appointments SET status = @Status, ApprovalRemarks = @Remarks WHERE appointment_id = @Id";
                using (SqlCommand updateCmd = new(query, conn))
                {
                    updateCmd.Parameters.AddWithValue("@Id", id);
                    updateCmd.Parameters.AddWithValue("@Status", newStatus);
                    updateCmd.Parameters.AddWithValue("@Remarks", string.IsNullOrEmpty(remarks) ? DBNull.Value : remarks);
                    await updateCmd.ExecuteNonQueryAsync();
                }

                // Send emails only for specific transitions
                if (!string.IsNullOrEmpty(appointment.BookerEmail))
                {
                    if (newStatus == "Approved")
                    {
                        await SendApprovalEmail(appointment, professorName);
                    }
                    else if (newStatus == "Cancelled" && currentStatus == "Approved")
                    {
                        await SendCancellationEmail(appointment, professorName);
                    }
                    else if (newStatus == "Declined")
                    {
                        await SendDeclinedEmail(appointment, professorName);
                    }
                }

                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating appointment: {ex.Message}");
                Message = "Error updating appointment status";
                return Page();
            }
        }

        private async Task SendCancellationEmail(Appointment appointment, string professorName)
        {
            try
            {
                var emailConfig = _configuration.GetSection("EmailConfig");
                using var client = new SmtpClient(emailConfig["SmtpServer"], int.Parse(emailConfig["SmtpPort"]))
                {
                    Credentials = new NetworkCredential(emailConfig["Username"], emailConfig["Password"]),
                    EnableSsl = true
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(emailConfig["FromEmail"], emailConfig["FromName"]),
                    Subject = $"Cancelled: Your Appointment with {professorName}",
                    Body = $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
    <h2 style='color: #d9534f;'>Appointment Cancellation</h2>
    
    <p>Dear {appointment.BookerName},</p>
    
    <p>Your approved appointment with <strong>{professorName}</strong> has been cancelled.</p>
    
    <h3 style='margin-top: 20px;'>Appointment Details:</h3>
    <ul>
        <li><strong>Date:</strong> {appointment.AppointmentDate.ToString("MMMM d, yyyy")}</li>
        <li><strong>Time:</strong> {appointment.FormattedAppointmentTime}</li>
        <li><strong>Purpose:</strong> {appointment.Purpose}</li>
        <li><strong>Mode:</strong> {appointment.Mode}</li>
    </ul>
    
    {(string.IsNullOrEmpty(appointment.ApprovalRemarks) ? "" : $@"
    <h3 style='margin-top: 20px;'>Cancellation Reason:</h3>
    <div style='background-color: #f8f9fa; padding: 15px; border-left: 4px solid #d9534f; margin-bottom: 20px;'>
        {appointment.ApprovalRemarks}
    </div>
    ")}
    
    <p>If you have any questions or would like to reschedule, please contact {professorName} directly.</p>
    
    <p style='margin-top: 30px;'>We apologize for any inconvenience.</p>
    
    <p>Sincerely,<br>
    <strong>CICT Appointment Suport Team</strong></p>
</div>",
                    IsBodyHtml = true
                };

                mailMessage.To.Add(appointment.BookerEmail);
                await client.SendMailAsync(mailMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending cancellation email: {ex.Message}");
                throw;
            }
        }

        public async Task<IActionResult> OnPostComplete(int id, string remarks)
        {
            try
            {
                var result = await UpdateAppointmentStatus(id, "Completed", remarks);
                return RedirectToPage(new { action = "complete", success = "true" });
            }
            catch
            {
                return RedirectToPage(new { action = "complete", success = "false" });
            }
        }

        private async Task<(Appointment, string)> GetAppointmentDetails(SqlConnection conn, int appointmentId)
        {
            string query = @"
SELECT 
    a.appointment_id,
    a.appointment_date,
    a.appointment_time,
    a.purpose,
    a.Mode,
    a.additionalnotes,
    COALESCE(s.email, al.email) AS booker_email,
    COALESCE(s.first_name + ' ' + s.last_name, al.first_name + ' ' + al.last_name) AS booker_name,
    c.first_name + ' ' + c.last_name AS professor_name,
    a.status
FROM dbo.Appointments a
LEFT JOIN dbo.userStudents s ON a.student_id = s.student_id
LEFT JOIN dbo.userAlumni al ON a.alumni_id = al.alumni_id
JOIN dbo.userCICT c ON a.professor_id = c.professor_id
WHERE a.appointment_id = @Id";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Id", appointmentId);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (reader.Read())
                    {
                        var appointment = new Appointment
                        {
                            Id = reader.GetInt32(0),
                            AppointmentDate = reader.GetDateTime(1),
                            AppointmentTime = reader.GetTimeSpan(2),
                            Purpose = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                            Mode = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                            AdditionalNotes = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                            BookerEmail = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                            BookerName = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                            Status = reader.IsDBNull(8) ? string.Empty : reader.GetString(8)
                        };
                        string professorName = reader.IsDBNull(8) ? string.Empty : reader.GetString(8);
                        return (appointment, professorName);
                    }
                }
            }
            return (null, null);
        }

        private async Task SendApprovalEmail(Appointment appointment, string professorName)
        {
            try
            {
                var emailConfig = _configuration.GetSection("EmailConfig");
                var smtpServer = emailConfig["SmtpServer"];
                var smtpPort = int.Parse(emailConfig["smtpPort"]);
                var username = emailConfig["Username"];
                var password = emailConfig["Password"];
                var fromEmail = emailConfig["FromEmail"];
                var fromName = emailConfig["FromName"] ?? "CICT Appointment System";

                using var client = new SmtpClient(smtpServer, smtpPort)
                {
                    EnableSsl = true,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(username, password),
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(fromEmail, fromName),
                    Subject = $"Appointment Approved with {professorName}",
                    Body = $@"
        <h3>Dear {appointment.BookerName},</h3>
        <p>I am pleased to inform you that your appointment request has been <span style='color:green; font-weight:bold;'>approved</span> by {professorName}.</p>
        
        <h4>Appointment Details:</h4>
        <ul>
            <li><strong>Faculty:</strong> {professorName}</li>
            <li><strong>Date:</strong> {appointment.AppointmentDate.ToString("MMM dd, yyyy")}</li>
            <li><strong>Time:</strong> {appointment.FormattedAppointmentTime}</li>
            <li><strong>Purpose:</strong> {appointment.Purpose}</li>
            <li><strong>Mode:</strong> {appointment.Mode}</li>
            <li><strong>Your Notes:</strong> {appointment.AdditionalNotes}</li>
        </ul>
        
       {(string.IsNullOrEmpty(appointment.ApprovalRemarks) ? "" : $@"
<h4>Professor's Remarks:</h4>
<p style='background-color:#f8f9fa; padding:10px; border-left:3px solid #28a745;'>
    {appointment.ApprovalRemarks}
</p>
")}
        
        <p>Please make sure to be 5 minutes early at the scheduled time.</p>
        <p>Best Regards,<br><strong>CICT Appointment System</strong></p>",
                    IsBodyHtml = true
                };

                mailMessage.To.Add(appointment.BookerEmail);

                await client.SendMailAsync(mailMessage);
                _logger.LogInformation($"Approval email sent to {appointment.BookerEmail}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending approval email: {ex.Message}");
                _logger.LogError($"Stack Trace: {ex.StackTrace}");
                throw;
            }
        }

        private async Task SendDeclinedEmail(Appointment appointment, string professorName)
        {
            try
            {
                var emailConfig = _configuration.GetSection("EmailConfig");
                var smtpServer = emailConfig["SmtpServer"];
                var smtpPort = int.Parse(emailConfig["smtpPort"]);
                var username = emailConfig["Username"];
                var password = emailConfig["Password"];
                var fromEmail = emailConfig["FromEmail"];
                var fromName = emailConfig["FromName"] ?? "CICT Appointment System";

                using var client = new SmtpClient(smtpServer, smtpPort)
                {
                    EnableSsl = true,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(username, password),
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 10000
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(fromEmail, fromName),
                    Subject = $"Appointment Declined with {professorName}",
                    Body = $@"
        <h3>Dear {appointment.BookerName},</h3>
        <p>We regret to inform you that your appointment request with {professorName} has been <span style='color:red; font-weight:bold;'>declined</span>.</p>
        
        <h4>Appointment Details:</h4>
        <ul>
            <li><strong>Professor:</strong> {professorName}</li>
            <li><strong>Date:</strong> {appointment.AppointmentDate.ToString("MMM dd, yyyy")}</li>
            <li><strong>Time:</strong> {appointment.FormattedAppointmentTime}</li>
            <li><strong>Purpose:</strong> {appointment.Purpose}</li>
            <li><strong>Mode:</strong> {appointment.Mode}</li>
            <li><strong>Your Notes:</strong> {appointment.AdditionalNotes}</li>
        </ul>
        
      {(string.IsNullOrEmpty(appointment.ApprovalRemarks) ? "" : $@"
        <h4>Professor's Remarks:</h4>
        <p style='background-color:#f8f9fa; padding:10px; border-left:3px solid #dc3545;'>
            {appointment.ApprovalRemarks}
        </p>
        ")}

        
        <p>If you believe this was declined in error or would like to discuss alternative arrangements, 
        please feel free to contact {professorName} directly.</p>
        
        <p>We apologize for any inconvenience this may cause.</p>
        
        <p>Best Regards,<br><strong>CICT Appointment System</strong></p>",
                    IsBodyHtml = true
                };

                mailMessage.To.Add(appointment.BookerEmail);

                await client.SendMailAsync(mailMessage);
                _logger.LogInformation($"Declined email sent to {appointment.BookerEmail}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending declined email: {ex.Message}");
                _logger.LogError($"Stack Trace: {ex.StackTrace}");
                throw;
            }
        }



        public async Task<IActionResult> OnPostLogout()
        {
            await HttpContext.SignOutAsync();
            return RedirectToPage("/Login");
        }
    }
}
