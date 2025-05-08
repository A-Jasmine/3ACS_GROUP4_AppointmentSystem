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
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace WebBookingApp.Pages
{
    public class manageRegistrationModel : PageModel
    {
        private readonly ILogger<manageRegistrationModel> _logger;
        private readonly string connString;
        private readonly IConfiguration _configuration;

        // Email configuration using appsettings.json or environment variables
        private readonly string smtpServer;
        private readonly int smtpPort;
        private readonly string emailUsername;
        private readonly string emailPassword;
        private readonly string fromEmail;

        public string FirstName { get; private set; } = "";
        public byte[]? ProfilePicture { get; private set; }
        public List<PendingUser> PendingUsers { get; set; } = new List<PendingUser>();

        [BindProperty] public IFormFile? UploadedFile { get; set; }
        public string Message { get; private set; } = "";

        public manageRegistrationModel(ILogger<manageRegistrationModel> logger, IConfiguration configuration)
        {
            _logger = logger;

            // Accessing the Email Configuration from appsettings.json
            smtpServer = configuration["EmailConfig:SmtpServer"];
            smtpPort = int.Parse(configuration["EmailConfig:SmtpPort"]);
            emailUsername = configuration["EmailConfig:Username"];
            emailPassword = configuration["EmailConfig:Password"];
            fromEmail = configuration["EmailConfig:FromEmail"];

            // Log the email configuration for debugging
            _logger.LogInformation($"SMTP Server: {smtpServer}, Port: {smtpPort}, FromEmail: {fromEmail}");

            // Initialize connection string from appsettings.json
            connString = configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrEmpty(connString))
            {
                _logger.LogError("Connection string is not properly configured.");
                throw new InvalidOperationException("Connection string is not properly configured.");
            }

        }


        private void SendEmail(string toEmail, string subject, string body)
        {
            if (string.IsNullOrEmpty(toEmail))
            {
                _logger.LogWarning("Attempted to send email, but recipient email is null or empty.");
                return;
            }

            var smtpClient = new SmtpClient(smtpServer)
            {
                Port = smtpPort,  // Ensure this matches your configuration (465 or 587)
                Credentials = new NetworkCredential(emailUsername, emailPassword),
                EnableSsl = true,  // Set to true for SSL/TLS
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(fromEmail),
                Subject = subject,
                Body = body,
                IsBodyHtml = true,
            };
            mailMessage.To.Add(toEmail);

            try
            {
                _logger.LogInformation($"Sending email to: {toEmail}");
                smtpClient.Send(mailMessage);
                _logger.LogInformation("Email sent successfully.");
            }
            catch (SmtpException smtpEx)
            {
                _logger.LogError($"SMTP Error while sending email to {toEmail}: {smtpEx.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send email to {toEmail}: {ex.Message}");
            }
        }

        private string GetUserRole(int userId, SqlConnection connection)
        {
            string role = string.Empty;

            string query = "SELECT role FROM userVerification WHERE id = @userId";

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@userId", userId);
                _logger.LogInformation("Executing query to get user role for userId: " + userId);
                var result = command.ExecuteScalar();

                // Check if result is not DBNull.Value before accessing
                role = result != DBNull.Value ? result.ToString() : string.Empty;
            }

            _logger.LogInformation("User role for userId " + userId + " is " + role);
            return role;
        }



        public IActionResult OnPostApprove(int userId)
        {
            _logger.LogInformation("Approving user with ID: " + userId);
            string userEmail = string.Empty;

            using (SqlConnection connection = new SqlConnection(connString))
            {
                connection.Open();
                _logger.LogInformation("Database connection opened.");

                // Query to get the user's email
                string getEmailQuery = "SELECT email FROM userVerification WHERE id = @userId";

                using (SqlCommand command = new SqlCommand(getEmailQuery, connection))
                {
                    command.Parameters.AddWithValue("@userId", userId);
                    _logger.LogInformation("Executing query to fetch email for userId: " + userId);

                    var result = command.ExecuteScalar();
                    userEmail = result != DBNull.Value ? result.ToString() : string.Empty;
                }

                if (string.IsNullOrEmpty(userEmail))
                {
                    _logger.LogWarning("Email not found or is null for userId: " + userId);
                    return new JsonResult(new { success = false, message = "Email not found for this user." });
                }

                // Fetch the role of the user
                string userRole = GetUserRole(userId, connection);

                if (string.IsNullOrEmpty(userRole))
                {
                    _logger.LogWarning("Role is null or empty for userId: " + userId);
                    return new JsonResult(new { success = false, message = "User role not found." });
                }

                // Insert into the correct table based on user role
                string insertQuery = userRole == "Student" ? @"
            INSERT INTO userStudents (student_id, first_name, middle_name, last_name, email, program, mobile_number, password, role)
            SELECT student_id, first_name, middle_name, last_name, email, program, mobile_number, password, role
            FROM userVerification WHERE id = @userId AND student_id IS NOT NULL"
                    : @"
            INSERT INTO userAlumni (alumni_id, first_name, middle_name, last_name, email, program, mobile_number, password, role)
            SELECT alumni_id, first_name, middle_name, last_name, email, program, mobile_number, password, role
            FROM userVerification WHERE id = @userId AND alumni_id IS NOT NULL";

                using (SqlCommand command = new SqlCommand(insertQuery, connection))
                {
                    command.Parameters.AddWithValue("@userId", userId);
                    _logger.LogInformation("Executing insert query to move user data to respective table.");
                    command.ExecuteNonQuery();
                }

                // Delete user from verification table
                string deleteQuery = "DELETE FROM userVerification WHERE id = @userId";
                using (SqlCommand command = new SqlCommand(deleteQuery, connection))
                {
                    command.Parameters.AddWithValue("@userId", userId);
                    _logger.LogInformation("Executing delete query to remove user from userVerification table.");
                    command.ExecuteNonQuery();
                }

                // Send approval email
                string approvalMessage =
                "Dear Student,<br><br>" +
                "Congratulations! We are pleased to inform you that your registration for the <strong>CICT Appointment System</strong> has been approved. " +
                "Your student ID has been successfully verified, and you can now log in to book appointments with [Professors / the Dean / Student Organizations].<br><br>" +
                "If you have any questions or need assistance, please don't hesitate to reach out to us.<br><br>" +
                "Best regards,<br>" +
                "<strong>CICT Appointment System</strong>";

                _logger.LogInformation("Sending approval email to user: " + userEmail);
                SendEmail(userEmail, "Registration Approved – CICT Appointment System", approvalMessage);
            }

            return RedirectToPage(); // Refreshes current page
        }




        public IActionResult OnPostReject(int userId)
        {
            _logger.LogInformation("Rejecting user with ID: {UserId}", userId);
            string userEmail = string.Empty;

            try
            {
                using (SqlConnection connection = new SqlConnection(connString))
                {
                    connection.Open();
                    _logger.LogInformation("Database connection opened.");

                    // Get the user's email first
                    string getEmailQuery = "SELECT email FROM userVerification WHERE id = @userId";
                    using (SqlCommand getEmailCmd = new SqlCommand(getEmailQuery, connection))
                    {
                        getEmailCmd.Parameters.AddWithValue("@userId", userId);
                        object result = getEmailCmd.ExecuteScalar();
                        if (result != null)
                        {
                            userEmail = result.ToString();
                            _logger.LogInformation("Email found: {Email}", userEmail);
                        }
                        else
                        {
                            _logger.LogWarning("No user found with ID: {UserId}", userId);
                            return RedirectToPage();
                        }
                    }

                    // Move user data to Rejected_Users table
                    string insertQuery = @"
                    INSERT INTO Rejected_Users (
                        student_id, alumni_id, first_name, middle_name, last_name, email,
                        program, year_level, mobile_number, password, role, id_picture
                    )
                    SELECT 
                        student_id, alumni_id, first_name, middle_name, last_name, email,
                        program, year_level, mobile_number, password, role, id_picture
                    FROM userVerification
                    WHERE id = @userId";

                    using (SqlCommand insertCmd = new SqlCommand(insertQuery, connection))
                    {
                        insertCmd.Parameters.AddWithValue("@userId", userId);
                        int rowsInserted = insertCmd.ExecuteNonQuery();
                        _logger.LogInformation("Rows inserted into Rejected_Users: {Rows}", rowsInserted);
                    }

                    // Delete user from userVerification table
                    string deleteQuery = "DELETE FROM userVerification WHERE id = @userId";
                    using (SqlCommand deleteCmd = new SqlCommand(deleteQuery, connection))
                    {
                        deleteCmd.Parameters.AddWithValue("@userId", userId);
                        int rowsDeleted = deleteCmd.ExecuteNonQuery();
                        _logger.LogInformation("Rows deleted from userVerification: {Rows}", rowsDeleted);
                    }

                    // Rejection email message (HTML-friendly version)
                    string rejectionMessage =
                    "Dear Student,<br><br>" +
                    "We regret to inform you that your registration for the CICT Appointment System has been declined. " +
                    "Our records indicate that the student ID you provided could not be verified.<br><br>" +
                    "If you believe this is an error, please double-check your student ID and try registering again. " +
                    "For further assistance, feel free to contact us.<br><br>" +
                    "Best regards,<br>" +
                    "<strong>CICT Appointment System</strong>";


                    // Send rejection email
                    SendEmail(userEmail, "Registration Rejected – CICT Appointment System", rejectionMessage);
                    _logger.LogInformation("Rejection email sent to: {Email}", userEmail);
                }
               }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rejecting user with ID: {UserId}", userId);
            }

            return RedirectToPage();
        }



        public void OnGet()
        {
            _logger.LogInformation("OnGet() started");

            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;

            // If the user is admin, use "admin" as the identifier (not email)
            if (string.IsNullOrEmpty(userEmail) || User.IsInRole("Admin"))
            {
                userEmail = "admin"; // Admin doesn't have a traditional email, so use "admin"
            }

            using SqlConnection conn = new(connString);
            conn.Open();
            _logger.LogInformation("Database connection opened.");

            // 🔹 Retrieve username for admin
            string getUsernameQuery = @"
        SELECT username 
        FROM dbo.userAdmin 
        WHERE username = @Username";

            using (SqlCommand cmd = new(getUsernameQuery, conn))
            {
                cmd.Parameters.AddWithValue("@Username", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    FirstName = reader["username"].ToString();
                    _logger.LogInformation($"Retrieved admin username: {FirstName}");
                }
                else
                {
                    _logger.LogWarning("No admin found with the specified username.");
                }
            }

            // 🔹 Fetch pending users
            _logger.LogInformation("Fetching pending users for registration.");
            string query = @"
    SELECT id, email, student_id, alumni_id, first_name, middle_name, last_name, 
           program, year_level, mobile_number, role, id_picture, id_picture2, status 
    FROM userVerification";

            using (SqlCommand command = new SqlCommand(query, conn))
            {
                try
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (!reader.HasRows)
                        {
                            _logger.LogInformation("No pending users found in the database.");
                        }

                        PendingUsers.Clear();

                        while (reader.Read())
                        {
                            var user = new PendingUser
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("id")),
                                IdPicture2 = reader.IsDBNull(reader.GetOrdinal("id_picture2")) ? null : (byte[])reader["id_picture2"],
                                Email = reader.IsDBNull(reader.GetOrdinal("email")) ? "" : reader.GetString(reader.GetOrdinal("email")),
                                StudentId = reader.IsDBNull(reader.GetOrdinal("student_id")) ? null : reader.GetString(reader.GetOrdinal("student_id")),
                                AlumniId = reader.IsDBNull(reader.GetOrdinal("alumni_id")) ? null : reader.GetString(reader.GetOrdinal("alumni_id")),
                                FirstName = reader.IsDBNull(reader.GetOrdinal("first_name")) ? "" : reader.GetString(reader.GetOrdinal("first_name")),
                                MiddleName = reader.IsDBNull(reader.GetOrdinal("middle_name")) ? null : reader.GetString(reader.GetOrdinal("middle_name")),
                                LastName = reader.IsDBNull(reader.GetOrdinal("last_name")) ? "" : reader.GetString(reader.GetOrdinal("last_name")),
                                Program = reader.IsDBNull(reader.GetOrdinal("program")) ? "" : reader.GetString(reader.GetOrdinal("program")),
                                YearLevel = reader.IsDBNull(reader.GetOrdinal("year_level")) ? null : reader.GetString(reader.GetOrdinal("year_level")),
                                MobileNumber = reader.IsDBNull(reader.GetOrdinal("mobile_number")) ? "" : reader.GetString(reader.GetOrdinal("mobile_number")),
                                Role = reader.IsDBNull(reader.GetOrdinal("role")) ? null : reader.GetString(reader.GetOrdinal("role")),
                                Status = reader.IsDBNull(reader.GetOrdinal("status")) ? "" : reader.GetString(reader.GetOrdinal("status")),
                                IdPicture = reader.IsDBNull(reader.GetOrdinal("id_picture")) ? null : (byte[])reader["id_picture"]
                            };

                            _logger.LogInformation($"Pending user: {user.FirstName} {user.LastName}, Role: {user.Role}, Email: {user.Email}");
                            PendingUsers.Add(user);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("Error while fetching pending users: " + ex.Message);
                }
            }
        }




        public IActionResult OnPostUpdateUserDetails(int userId, string firstName, string lastName, string yearLevel, string mobileNumber)
        {
            _logger.LogInformation("Updating user details for userId " + userId);
            using (SqlConnection connection = new SqlConnection(connString))
            {
                connection.Open();

                // SQL query to update the user details
                string updateQuery = @"
                    UPDATE userVerification
                    SET first_name = @FirstName, 
                        last_name = @LastName, 
                        year_level = @YearLevel, 
                        mobile_number = @MobileNumber
                    WHERE id = @UserId";

                using (SqlCommand command = new SqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@UserId", userId);
                    command.Parameters.AddWithValue("@FirstName", firstName);
                    command.Parameters.AddWithValue("@LastName", lastName);
                    command.Parameters.AddWithValue("@YearLevel", yearLevel);
                    command.Parameters.AddWithValue("@MobileNumber", mobileNumber);

                    try
                    {
                        int rowsAffected = command.ExecuteNonQuery();
                        if (rowsAffected > 0)
                        {
                            _logger.LogInformation("User details updated successfully for user ID " + userId);
                        }
                        else
                        {
                            _logger.LogWarning("No user found with ID " + userId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Error updating user details: " + ex.Message);
                        Message = "Error updating user details: " + ex.Message;
                        return Page();
                    }
                }
            }

            return RedirectToPage(); // Refresh the page after update
        }

        public async Task<IActionResult> OnPostLogout()
        {
            await HttpContext.SignOutAsync();
            _logger.LogInformation("User logged out.");
            return RedirectToPage("/Login");
        }

        public class PendingUser
        {
            public int Id { get; set; }
            public string Email { get; set; }
            public string StudentId { get; set; }
            public string AlumniId { get; set; }
            public string FirstName { get; set; }
            public string MiddleName { get; set; }
            public string LastName { get; set; }
            public string Program { get; set; }
            public string YearLevel { get; set; }
            public string MobileNumber { get; set; }
            public string Role { get; set; }
            public byte[] IdPicture { get; set; }

            public byte[] IdPicture2 { get; set; }
            public string Status { get; set; }
        }
    }
}
