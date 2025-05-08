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

namespace WebBookingApp.Pages
{
    public class manageStudentsModel : PageModel
    {
        private readonly ILogger<manageStudentsModel> _logger;
        private readonly string _connString;

        [BindProperty(SupportsGet = true)]
        public string? SelectedRole { get; set; }

        public string FirstName { get; private set; } = "";
        public byte[]? ProfilePicture { get; private set; }
        [BindProperty] public IFormFile? UploadedFile { get; set; }
        public string Message { get; private set; } = "";
        public List<Student> Students { get; private set; } = new();

        // 🔹 Ensure this property is populated from the query string
        [BindProperty(SupportsGet = true)]
        public string? SelectedYearLevel { get; set; }

        public manageStudentsModel(ILogger<manageStudentsModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connString = configuration.GetConnectionString("DefaultConnection");
        }

        public void OnGet()
        {
            _logger.LogInformation($"OnGet() started. SelectedYearLevel: {SelectedYearLevel}, SelectedRole: {SelectedRole}");

            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "admin";

            using SqlConnection conn = new(_connString);
            conn.Open();

            // Fetch admin profile data
            string query = "SELECT username FROM dbo.userAdmin WHERE username = @Username";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Username", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    FirstName = reader["username"].ToString();
                    _logger.LogInformation($"Retrieved Username: {FirstName}");
                }
            }

            // Fetch profile picture
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

            // Determine appropriate query based on SelectedRole and SelectedYearLevel
            if (!string.IsNullOrEmpty(SelectedYearLevel) && SelectedRole != "Alumni")
            {
                query = @"
            SELECT 
                student_id, 
                NULL AS alumni_id, 
                first_name, 
                middle_name, 
                last_name, 
                email, 
                mobile_number, 
                program, 
                YearLevel, 
                'Student' AS role 
            FROM dbo.userStudents 
            WHERE YearLevel = @YearLevel
            UNION 
            SELECT 
                alumni_id AS student_id, 
                alumni_id AS alumni_id, 
                first_name, 
                middle_name, 
                last_name, 
                email, 
                mobile_number, 
                program, 
                'N/A' AS YearLevel, 
                'Alumni' AS role 
            FROM dbo.userAlumni";
            }
            else if (SelectedRole == "Alumni")
            {
                query = @"
            SELECT 
                alumni_id AS student_id, 
                alumni_id AS alumni_id, 
                first_name, 
                middle_name, 
                last_name, 
                email, 
                mobile_number, 
                program, 
                'N/A' AS YearLevel, 
                'Alumni' AS role 
            FROM dbo.userAlumni";
            }
            else if (SelectedRole == "Student")
            {
                query = @"
            SELECT 
                student_id, 
                NULL AS alumni_id, 
                first_name, 
                middle_name, 
                last_name, 
                email, 
                mobile_number, 
                program, 
                YearLevel, 
                'Student' AS role 
            FROM dbo.userStudents";
            }
            else
            {
                query = @"
            SELECT 
                student_id, 
                NULL AS alumni_id, 
                first_name, 
                middle_name, 
                last_name, 
                email, 
                mobile_number, 
                program, 
                YearLevel, 
                'Student' AS role 
            FROM dbo.userStudents
            UNION 
            SELECT 
                alumni_id AS student_id, 
                alumni_id AS alumni_id, 
                first_name, 
                middle_name, 
                last_name, 
                email, 
                mobile_number, 
                program, 
                'N/A' AS YearLevel, 
                'Alumni' AS role 
            FROM dbo.userAlumni";
            }

            // Fetch combined student/alumni data
            using (SqlCommand cmd = new(query, conn))
            {
                if (!string.IsNullOrEmpty(SelectedYearLevel) && SelectedRole != "Alumni")
                {
                    cmd.Parameters.AddWithValue("@YearLevel", SelectedYearLevel);
                }

                using SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    Students.Add(new Student
                    {
                        StudentID = reader["student_id"].ToString(),
                        FirstName = reader["first_name"].ToString(),
                        MiddleName = reader["middle_name"]?.ToString(),
                        LastName = reader["last_name"].ToString(),
                        Email = reader["email"].ToString(),
                        MobileNumber = reader["mobile_number"].ToString(),
                        Program = reader["program"].ToString(),
                        YearLevel = reader["YearLevel"].ToString(),
                        Role = reader["role"].ToString()
                    });
                }
            }

            _logger.LogInformation($"Total students/alumni retrieved: {Students.Count}");
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

        public async Task<IActionResult> OnPostDeleteAsync(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                _logger.LogWarning("Delete failed: Student ID is null or empty.");
                return RedirectToPage(); // Could redirect to an error page
            }

            using SqlConnection conn = new(_connString);
            await conn.OpenAsync();

            try
            {
                // Try deleting from userStudents first
                string deleteStudentQuery = "DELETE FROM dbo.userStudents WHERE student_id = @Id";
                using (SqlCommand cmd = new(deleteStudentQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    int rowsAffected = await cmd.ExecuteNonQueryAsync();

                    // If no rows affected, try deleting from userAlumni
                    if (rowsAffected == 0)
                    {
                        string deleteAlumniQuery = "DELETE FROM dbo.userAlumni WHERE alumni_id = @Id";
                        using (SqlCommand alumniCmd = new(deleteAlumniQuery, conn))
                        {
                            alumniCmd.Parameters.AddWithValue("@Id", id);
                            await alumniCmd.ExecuteNonQueryAsync();
                        }
                    }
                }

                _logger.LogInformation($"Deleted record with ID: {id}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error deleting record with ID {id}: {ex.Message}");
            }

            return RedirectToPage();
        }


        public async Task<IActionResult> OnPostLogout()
        {
            await HttpContext.SignOutAsync();
            return RedirectToPage("/Login");
        }

        // 🔹 Define Student class inside manageStudentsModel
        public class Student
        {
            public string StudentID { get; set; }
            public string AlumniID { get; set; }
            public string FirstName { get; set; }
            public string MiddleName { get; set; }
            public string LastName { get; set; }
            public string Email { get; set; }
            public string MobileNumber { get; set; }
            public string Program { get; set; }
            public string YearLevel { get; set; }
            public string Role { get; set; }
        }
    }
}
