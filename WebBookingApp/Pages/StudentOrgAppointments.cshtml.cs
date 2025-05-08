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
using System;

namespace WebBookingApp.Pages
{
    public class StudentOrgAppointmentsModel : PageModel
    {
        private readonly ILogger<StudentOrgAppointmentsModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly string connString;

        [BindProperty(SupportsGet = true)]  // This allows the filter to work with GET requests
        public string StatusFilter { get; set; } = "All";  // Default value

        public string FirstName { get; private set; } = "";
        public string LastName { get; private set; } = "";
        public string Role { get; set; } = "";
        public byte[]? ProfilePicture { get; private set; }
        public string StudentOrganizationName { get; private set; } = "N/A";
        public string StudentOrgRole { get; private set; } = "N/A";
        public string StudentID { get; private set; } = "";

        [BindProperty] public IFormFile? UploadedFile { get; set; }
        public string Message { get; private set; } = "";
        [BindProperty] public string ApprovalRemark { get; set; } = "";

        public class OrganizationPendingAppointment
        {
            public int AppointmentID { get; set; }
            public string BookerName { get; set; } = "";
            public string OrganizationName { get; set; } = "";
            public DateTime AppointmentDate { get; set; }
            public TimeSpan AppointmentTime { get; set; }
            public string Purpose { get; set; } = "";
            public string Status { get; set; } = "";
            public string Mode { get; set; } = "";
            public string AdditionalNotes { get; set; } = "";
            public string BookerProfilePicture { get; set; } = "";
            public string BookerEmail { get; set; } = ""; // Added for email notification
            public string FormattedAppointmentTime => DateTime.Today.Add(AppointmentTime).ToString("hh:mm tt");
            public string YearSection { get; set; } = "";
        }

        public List<OrganizationPendingAppointment> OrganizationPendingAppointments { get; private set; } = new();

        public StudentOrgAppointmentsModel(ILogger<StudentOrgAppointmentsModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            connString = _configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Database connection string not configured.");
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

            // Fetch user details
            string query = @"
                SELECT 
                    us.first_name, 
                    us.last_name, 
                    us.role, 
                    us.student_id,
                    org.OrganizationName, 
                    om.Role AS OrgRole
                FROM dbo.userStudents us
                LEFT JOIN dbo.OrganizationMembers om ON us.student_id = om.StudentId
                LEFT JOIN dbo.StudentOrganizations org ON om.OrganizationID = org.OrganizationID
                WHERE us.email = @Email

                UNION

                SELECT 
                    ua.first_name, 
                    ua.last_name, 
                    ua.role,
                    NULL AS student_id,
                    NULL AS OrganizationName, 
                    NULL AS OrgRole
                FROM dbo.userAlumni ua
                WHERE ua.email = @Email";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    FirstName = reader["first_name"].ToString();
                    LastName = reader["last_name"].ToString();
                    Role = reader["role"].ToString();
                    StudentID = reader["student_id"]?.ToString() ?? "";
                    StudentOrganizationName = reader["OrganizationName"]?.ToString() ?? "N/A";
                    StudentOrgRole = reader["OrgRole"]?.ToString() ?? "N/A";
                }
            }

        

            // Retrieve profile picture
            query = "SELECT Picture FROM dbo.userPictures WHERE email = @Email";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read() && reader["Picture"] != DBNull.Value)
                {
                    ProfilePicture = (byte[])reader["Picture"];
                }
            }

            // Get appointments for Student Org Members
            if (Role == "StudentOrgMember" || (!string.IsNullOrEmpty(StudentOrgRole) && StudentOrgRole != "N/A"))
            {
                query = @"
        SELECT 
            so.AppointmentID,
            s.first_name + ' ' + s.last_name AS booker_name,
            s.email AS booker_email,
            so.AppointmentDate,
            so.AppointmentTime,
            so.Purpose,
            so.Status,
            so.Mode,
            so.AdditionalNotes,
            so.Year_Section,
            up.Picture AS booker_picture,
            org.OrganizationName
        FROM dbo.StudentOrganizationAppointments so
        JOIN dbo.userStudents s ON so.StudentID = s.student_id
        LEFT JOIN dbo.userPictures up ON s.email = up.email
        LEFT JOIN dbo.OrganizationMembers om ON so.OrganizationMemberID = om.StudentId
        LEFT JOIN dbo.StudentOrganizations org ON om.OrganizationID = org.OrganizationID
        WHERE so.OrganizationMemberID = @StudentID";

                // Add status filter if not "All"
                if (!string.IsNullOrEmpty(StatusFilter) && StatusFilter != "All")
                {
                    query += " AND so.Status = @Status";
                }

                query += " ORDER BY so.AppointmentDate DESC, so.AppointmentTime DESC";

                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@StudentID", StudentID);

                    if (!string.IsNullOrEmpty(StatusFilter) && StatusFilter != "All")
                    {
                        cmd.Parameters.AddWithValue("@Status", StatusFilter);
                    }

                    using SqlDataReader reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        byte[]? bookerPicture = reader["booker_picture"] as byte[];
                        string base64BookerPicture = bookerPicture != null
                            ? $"data:image/jpeg;base64,{Convert.ToBase64String(bookerPicture)}"
                            : "/images/image.png";

                        OrganizationPendingAppointments.Add(new OrganizationPendingAppointment
                        {
                            AppointmentID = Convert.ToInt32(reader["AppointmentID"]),
                            BookerName = reader["booker_name"].ToString(),
                            BookerEmail = reader["booker_email"].ToString(),
                            OrganizationName = reader["OrganizationName"].ToString(),
                            AppointmentDate = Convert.ToDateTime(reader["AppointmentDate"]),
                            AppointmentTime = (TimeSpan)reader["AppointmentTime"],
                            Purpose = reader["Purpose"].ToString(),
                            Status = reader["Status"].ToString(),
                            Mode = reader["Mode"]?.ToString() ?? "N/A",
                            YearSection = reader["Year_Section"]?.ToString() ?? "",
                            AdditionalNotes = reader["AdditionalNotes"]?.ToString() ?? "None",
                            BookerProfilePicture = base64BookerPicture
                        });
                    }
                }
            }

        }

        public async Task<IActionResult> OnPostApprove(int appointmentId, string approvalRemark)
        {
            return await UpdateAppointmentStatus(appointmentId, "Approved", approvalRemark);
        }

        public async Task<IActionResult> OnPostDecline(int appointmentId, string approvalRemark)
        {
            return await UpdateAppointmentStatus(appointmentId, "Declined", approvalRemark);
        }

        private async Task<IActionResult> UpdateAppointmentStatus(int appointmentId, string status, string remark)
        {
            try
            {
                using SqlConnection conn = new(connString);
                await conn.OpenAsync();

                // First get appointment details including student email and org member name
                string getQuery = @"
SELECT 
    so.AppointmentID,
    s.first_name + ' ' + s.last_name AS booker_name,
    s.email AS booker_email,
    so.AppointmentDate,
    so.AppointmentTime,
    so.Purpose,
    so.Mode,
    org.OrganizationName,
    om.Role AS org_member_role,
    -- Get organization member's name from userStudents table
    omStudent.first_name + ' ' + omStudent.last_name AS org_member_name
FROM dbo.StudentOrganizationAppointments so
JOIN dbo.userStudents s ON so.StudentID = s.student_id
JOIN dbo.OrganizationMembers om ON so.OrganizationMemberID = om.StudentId
JOIN dbo.userStudents omStudent ON om.StudentId = omStudent.student_id
LEFT JOIN dbo.StudentOrganizations org ON om.OrganizationID = org.OrganizationID
WHERE so.AppointmentID = @AppointmentID";

                string bookerEmail = "";
                string bookerName = "";
                string orgMemberName = "";
                DateTime? appointmentDate = null;
                TimeSpan? appointmentTime = null;
                string purpose = "";
                string mode = "";
                string orgName = "";
                string orgMemberRole = "";

                using (SqlCommand cmd = new(getQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@AppointmentID", appointmentId);
                    using SqlDataReader reader = await cmd.ExecuteReaderAsync();
                    if (reader.Read())
                    {
                        bookerEmail = reader["booker_email"].ToString();
                        bookerName = reader["booker_name"].ToString();
                        orgMemberName = reader["org_member_name"].ToString();
                        orgName = reader["OrganizationName"].ToString();
                        orgMemberRole = reader["org_member_role"].ToString();
                        appointmentDate = reader["AppointmentDate"] as DateTime?;
                        appointmentTime = reader["AppointmentTime"] as TimeSpan?;
                        purpose = reader["Purpose"].ToString();
                        mode = reader["Mode"].ToString();
                    }
                    else
                    {
                        TempData["Error"] = "Appointment not found.";
                        return RedirectToPage();
                    }
                }

                // Now update the appointment status
                string updateQuery = @"
UPDATE dbo.StudentOrganizationAppointments 
SET Status = @Status, 
    UpdatedAt = GETDATE(),
    AdditionalNotes = CASE 
        WHEN @Remark IS NOT NULL AND @Remark <> '' 
        THEN AdditionalNotes + CHAR(13) + CHAR(10) + 'Approver Remark: ' + @Remark 
        ELSE AdditionalNotes 
    END
WHERE AppointmentID = @AppointmentID";

                using (SqlCommand cmd = new(updateQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@Status", status);
                    cmd.Parameters.AddWithValue("@AppointmentID", appointmentId);
                    cmd.Parameters.AddWithValue("@Remark", remark ?? "");

                    int rowsAffected = await cmd.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        // Send email notification
                        if (!string.IsNullOrEmpty(bookerEmail))
                        {
                            string subject, body;
                            string formattedTime = appointmentTime.HasValue
                                ? DateTime.Today.Add(appointmentTime.Value).ToString("hh:mm tt")
                                : "";

                            if (status == "Approved")
                            {
                                subject = $"Your Appointment with {orgMemberName} {orgName} - {orgMemberRole} has been Approved";
                                body = $@"
                        <h3>Dear {bookerName},</h3>
                        <p>We're pleased to inform you that your appointment request with <strong>{orgMemberName}</strong> has been <span style='color:green; font-weight:bold;'>approved</span>.</p>
                        <p><strong>Appointment Details:</strong></p>
                        <ul>
                            <li><strong>Date:</strong> {appointmentDate?.ToString("MMMM dd, yyyy")}</li>
                            <li><strong>Time:</strong> {formattedTime}</li>
                            <li><strong>Purpose:</strong> {purpose}</li>
                            <li><strong>Mode:</strong> {mode}</li>
                        </ul>";

                                if (!string.IsNullOrEmpty(remark))
                                {
                                    body += $@"<p><strong>Note from {orgMemberName}:</strong><br>{remark}</p>";
                                }

                                body += $@"
                        <p>Please make sure to arrive on time for your appointment. If you need to reschedule or cancel, please contact us in advance.</p>
                        <p>We look forward to seeing you!</p>
                        <p>Best regards,</p>
                        <p><strong>{orgMemberName} ({@orgName.ToUpper()} - {orgMemberRole})</strong><br>
                        {_configuration["EmailConfig:FromName"]}</p>";
                            }
                            else if (status == "Declined")
                            {
                                subject = $"Your Appointment with {orgMemberName} has been Declined";
                                body = $@"
                        <h3>Dear {bookerName},</h3>
                        <p>We regret to inform you that your appointment request with <strong>{orgMemberName}</strong> has been <span style='color:red; font-weight:bold;'>declined</span>.</p>
                        <p><strong>Original Request Details:</strong></p>
                        <ul>
                            <li><strong>Date:</strong> {appointmentDate?.ToString("MMMM dd, yyyy")}</li>
                            <li><strong>Time:</strong> {formattedTime}</li>
                            <li><strong>Purpose:</strong> {purpose}</li>
                        </ul>";

                                if (!string.IsNullOrEmpty(remark))
                                {
                                    body += $@"<p><strong>Note from {orgMemberName}:</strong><br>{remark}</p>";
                                }

                                body += $@"
                        <p>We apologize for any inconvenience this may cause. You may want to:</p>
                        <ul>
                            <li>Modify your request and submit a new appointment</li>
                            <li>Contact {orgMemberName} directly for alternative arrangements</li>
                            <li>Check for other available time slots</li>
                        </ul>
                        <p>Thank you for your understanding.</p>
                        <p>Sincerely,</p>
                        <p><strong>{orgMemberName} ({@orgName.ToUpper()} - {orgMemberRole})</strong><br>
                        {_configuration["EmailConfig:FromName"]}</p>";
                            }
                            else if (status == "Cancelled")
                            {
                                subject = $"Your Appointment with {orgMemberName} has been Cancelled";
                                body = $@"
                        <h3>Dear {bookerName},</h3>
                        <p>We regret to inform you that your approved appointment with <strong>{orgMemberName}</strong> has been <span style='color:red; font-weight:bold;'>cancelled</span>.</p>
                        <p><strong>Original Appointment Details:</strong></p>
                        <ul>
                            <li><strong>Date:</strong> {appointmentDate?.ToString("MMMM dd, yyyy")}</li>
                            <li><strong>Time:</strong> {formattedTime}</li>
                            <li><strong>Purpose:</strong> {purpose}</li>
                            <li><strong>Mode:</strong> {mode}</li>
                        </ul>";

                                if (!string.IsNullOrEmpty(remark))
                                {
                                    body += $@"<p><strong>Cancellation Reason:</strong><br>{remark}</p>";
                                }

                                body += $@"
                        <p>We apologize for any inconvenience this may cause. You may want to:</p>
                        <ul>
                            <li>Request a new appointment at a different time</li>
                            <li>Contact {orgMemberName} directly for alternative arrangements</li>
                        </ul>
                        <p>Thank you for your understanding.</p>
                        <p>Sincerely,</p>
                        <p><strong>{orgMemberName} ({@orgName.ToUpper()} - {orgMemberRole})</strong><br>
                        {_configuration["EmailConfig:FromName"]}</p>";
                            }
                            else // Completed
                            {
                                subject = $"Your Appointment with {orgMemberName} has been Marked as Completed";
                                body = $@"
                        <h3>Dear {bookerName},</h3>
                        <p>Your appointment with <strong>{orgMemberName}</strong> has been marked as <span style='color:green; font-weight:bold;'>completed</span>.</p>
                        <p><strong>Appointment Details:</strong></p>
                        <ul>
                            <li><strong>Date:</strong> {appointmentDate?.ToString("MMMM dd, yyyy")}</li>
                            <li><strong>Time:</strong> {formattedTime}</li>
                            <li><strong>Purpose:</strong> {purpose}</li>
                        </ul>";

                                if (!string.IsNullOrEmpty(remark))
                                {
                                    body += $@"<p><strong>Note from {orgMemberName}:</strong><br>{remark}</p>";
                                }

                                body += $@"
                        <p>Thank you for your time. We hope the meeting was productive.</p>
                        <p>Best regards,</p>
                        <p><strong>{orgMemberName} ({@orgName.ToUpper()} - {orgMemberRole})</strong><br>
                        {_configuration["EmailConfig:FromName"]}</p>";
                            }

                            await SendEmailAsync(bookerEmail, subject, body);
                        }

                        TempData["Message"] = $"Appointment {status.ToLower()} successfully!";
                        if (!string.IsNullOrEmpty(bookerEmail))
                        {
                            TempData["Message"] += " Notification sent to student.";
                        }
                    }
                    else
                    {
                        TempData["Error"] = "Appointment not found or update failed.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating appointment status");
                TempData["Error"] = "An error occurred while updating the appointment.";
            }

            return RedirectToPage();
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
                var fromName = emailConfig["FromName"];

                using var client = new SmtpClient(smtpServer, smtpPort)
                {
                    Credentials = new NetworkCredential(username, password),
                    EnableSsl = true
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(fromEmail, fromName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

                mailMessage.To.Add(toEmail);

                await client.SendMailAsync(mailMessage);
                _logger.LogInformation($"Email sent to {toEmail}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending email: {ex.Message}");
                throw;
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
                return Page();
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
            var fileExtension = Path.GetExtension(UploadedFile.FileName).ToLower();
            if (!allowedExtensions.Contains(fileExtension))
            {
                Message = "Only image files (.jpg, .png, .gif) are allowed.";
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
                if (string.IsNullOrEmpty(userEmail)) return Page();

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

                return RedirectToPage("/dashboardStudent");
            }
            catch
            {
                Message = "Error uploading file.";
                return Page();
            }
        }


        public async Task<IActionResult> OnPostCancel(int appointmentId, string approvalRemark)
        {
            return await UpdateAppointmentStatus(appointmentId, "Cancelled", approvalRemark);
        }


        public async Task<IActionResult> OnPostComplete(int appointmentId, string approvalRemark)
        {
            return await UpdateAppointmentStatus(appointmentId, "Completed", approvalRemark);
        }

        public async Task<IActionResult> OnPostLogout()
        {
            await HttpContext.SignOutAsync();
            return RedirectToPage("/Login");
        }
    }
}