using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;

namespace WebBookingApp.Pages
{
    public class dashboardAdminModel : PageModel
    {
        private readonly ILogger<dashboardAdminModel> _logger;
        private readonly string _connString;

        public dashboardAdminModel(ILogger<dashboardAdminModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connString = configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrEmpty(_connString))
            {
                _logger.LogError("The connection string 'DefaultConnection' is missing in the configuration.");
                throw new InvalidOperationException("The ConnectionString property has not been initialized.");
            }
        }


        // Properties to hold user details and profile picture
        public string FirstName { get; private set; } = "";
        public byte[]? ProfilePicture { get; private set; }
        [BindProperty] public IFormFile? UploadedFile { get; set; }
        public string Message { get; private set; } = "";

        // Employee, Student, and Alumni Statistics
        public int TotalEmployees { get; set; }
        public int TotalStudents { get; set; }
        public int TotalAlumni { get; set; }
        public int TotalPendingRegistrations { get; set; }
        public int UpcomingAppointments { get; set; }

        public class StudentOrganization
        {
            public string OrganizationName { get; set; } = string.Empty;
            public int MemberCount { get; set; }
        }

        public List<StudentOrganization> StudentOrganizationData { get; set; } = new List<StudentOrganization>();

  

        public async Task OnGetAsync()
        {
            _logger.LogInformation("OnGet() started");

            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;

            if (string.IsNullOrEmpty(userEmail) || User.IsInRole("Admin"))
            {
                userEmail = "admin"; // Admin doesn't have a traditional email, so use "admin"
            }

            using SqlConnection conn = new(_connString);
            await conn.OpenAsync();

            // Fetching admin username for display
            string query = @"
                SELECT username 
                FROM dbo.userAdmin 
                WHERE username = @Username";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Username", userEmail);
                using SqlDataReader reader = await cmd.ExecuteReaderAsync();
                if (reader.Read())
                {
                    FirstName = reader["username"].ToString();
                    _logger.LogInformation($"Retrieved Username: {FirstName}");
                }
                else
                {
                    _logger.LogWarning("No admin found with the specified username.");
                }
            }

            // Fetching profile picture
            query = "SELECT Picture FROM dbo.userPictures WHERE email = @Email";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = await cmd.ExecuteReaderAsync();
                if (reader.Read() && reader["Picture"] != DBNull.Value)
                {
                    ProfilePicture = (byte[])reader["Picture"];
                    _logger.LogInformation("Profile picture retrieved successfully.");
                }
                else
                {
                    _logger.LogWarning("No profile picture found.");
                }
            }

            // Employee Statistics Query
            string employeeQuery = @"
                SELECT COUNT(*) FROM dbo.userCICT;";

            using (SqlCommand cmd = new SqlCommand(employeeQuery, conn))
            {
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync()) TotalEmployees = reader.GetInt32(0);
                }
            }

            // Student Statistics Query
            string studentQuery = @"
                SELECT COUNT(*) FROM dbo.userStudents;";
            using (SqlCommand cmd = new SqlCommand(studentQuery, conn))
            {
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync()) TotalStudents = reader.GetInt32(0);
                }
            }

            // Alumni Statistics Query
            string alumniQuery = @"
                SELECT COUNT(*) FROM dbo.userAlumni;";

            using (SqlCommand cmd = new SqlCommand(alumniQuery, conn))
            {
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync()) TotalAlumni = reader.GetInt32(0);
                }
            }

            // Pending Registrations Query (from dbo.userVerification table)
            string pendingQuery = @"
                SELECT COUNT(*) FROM dbo.userVerification WHERE status = 'Pending';";

            using (SqlCommand cmd = new SqlCommand(pendingQuery, conn))
            {
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync()) TotalPendingRegistrations = reader.GetInt32(0);
                }
            }

            // Fetching upcoming appointments (appointments scheduled for today or in the future)
            string appointmentQuery = @"
            SELECT COUNT(*) 
            FROM dbo.Appointments 
            WHERE appointment_date >= @TodayDate"; // Adjust the column name to match your table
            using (SqlCommand cmd = new SqlCommand(appointmentQuery, conn))
            {
                cmd.Parameters.AddWithValue("@TodayDate", DateTime.Now.Date);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync()) UpcomingAppointments = reader.GetInt32(0);
                }
            }

            // Fetching the student organization data based on member counts from dbo.OrganizationMembers using a JOIN
            string orgQuery = @"
            SELECT o.OrganizationName, COUNT(m.OrganizationID) AS MemberCount
            FROM dbo.OrganizationMembers m
            INNER JOIN dbo.StudentOrganizations o ON m.OrganizationID = o.OrganizationID
            GROUP BY o.OrganizationName
            ORDER BY MemberCount DESC;";

            using (SqlCommand cmd = new SqlCommand(orgQuery, conn))
            {
                using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                {
                    StudentOrganizationData = new List<StudentOrganization>();
                    while (await reader.ReadAsync())
                    {
                        var organization = new StudentOrganization
                        {
                            OrganizationName = reader["OrganizationName"].ToString(),
                            MemberCount = reader.GetInt32(reader.GetOrdinal("MemberCount"))
                        };
                        StudentOrganizationData.Add(organization);
                    }
                }
            }


        }


        public IActionResult OnGetProfilePicture()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;

            if (string.IsNullOrEmpty(userEmail) || User.IsInRole("Admin"))
            {
                userEmail = "admin";
            }

            using SqlConnection conn = new(_connString);
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

                if (string.IsNullOrEmpty(userEmail) || User.IsInRole("Admin"))
                {
                    userEmail = "admin";
                }

                using SqlConnection conn = new(_connString);
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

                return RedirectToPage("/dashboardAdmin");
            }
            catch (Exception ex)
            {
                Message = "Error uploading file: " + ex.Message;
                _logger.LogError("Error uploading profile picture: " + ex.Message);
                return Page();
            }
        }

        public async Task<IActionResult> OnPostLogout()
        {
            await HttpContext.SignOutAsync();
            return RedirectToPage("/Login");
        }
    }
}
