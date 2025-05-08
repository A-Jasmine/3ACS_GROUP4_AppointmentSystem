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
    public class viewEmployeeModel : PageModel
    {
        private readonly ILogger<viewEmployeeModel> _logger;
        private readonly string connString;
        private readonly IConfiguration _configuration;


        // Properties to hold data
        public List<Employee> Employees { get; set; } = new List<Employee>();

        // For Profile Picture (Existing Code)
        public byte[]? ProfilePicture { get; private set; }

        [BindProperty] public IFormFile? UploadedFile { get; set; }
        public string Message { get; private set; } = "";
        [BindProperty] public Employee SelectedEmployee { get; set; }

        public viewEmployeeModel(ILogger<viewEmployeeModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            connString = _configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Database connection string not configured.");
        }


        public void OnGet(string searchTerm = "")

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

            string query = @"
        SELECT 
            first_name, middle_name, last_name, 
            program, role, employment_status, joining_date, professor_id
        FROM dbo.userCICT";

            // Add search condition if searchTerm is provided
            if (!string.IsNullOrEmpty(searchTerm))
            {
                query += @"
        WHERE 
            first_name LIKE @SearchTerm OR
            middle_name LIKE @SearchTerm OR
            last_name LIKE @SearchTerm OR
            program LIKE @SearchTerm OR
            role LIKE @SearchTerm OR
            employment_status LIKE @SearchTerm";
            }



            using (SqlCommand cmd = new(query, conn))
            {

                if (!string.IsNullOrEmpty(searchTerm))
                {
                    cmd.Parameters.AddWithValue("@SearchTerm", $"%{searchTerm}%");
                }

                using SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    Employees.Add(new Employee
                    {
                        FirstName = reader["first_name"].ToString(),
                        MiddleName = reader["middle_name"].ToString(),
                        LastName = reader["last_name"].ToString(),
                        Program = reader["program"].ToString(),
                        Role = reader["role"].ToString(), // Role retrieved separately
                        EmploymentStatus = reader["employment_status"].ToString(),
                        JoiningDate = Convert.ToDateTime(reader["joining_date"]),
                        ProfessorId = Convert.ToInt32(reader["professor_id"])
                    });
                }
            }

            // Retrieve profile picture for admin from userPictures table (Existing Code)
            query = "SELECT Picture FROM dbo.userPictures WHERE email = @Email";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
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
        }

        // Model for Employee data
        public class Employee
        {
            public string FirstName { get; set; }
            public string MiddleName { get; set; }
            public string LastName { get; set; }
            public string Program { get; set; }
            public string Role { get; set; } // Added Role for separate handling
            public string EmploymentStatus { get; set; }
            public DateTime JoiningDate { get; set; }
            public int ProfessorId { get; set; }

            // New Properties
            public string ContactNumber { get; set; }
            public string Gender { get; set; }
            public DateTime DateOfBirth { get; set; }
            public string EmailAddress { get; set; }
            public string Address { get; set; }
            public string City { get; set; }

            // Full Name combined for the table
            public string FullName => $"{FirstName} {MiddleName} {LastName}";
        }


        // For Profile Picture Handling (Existing Code)
        public IActionResult OnGetProfilePicture()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;

            // If the user is admin, use "admin" as email
            if (string.IsNullOrEmpty(userEmail) || User.IsInRole("Admin"))
            {
                userEmail = "admin"; // Admin does not have a traditional email, so use "admin"
            }

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
        public async Task<IActionResult> OnPostAsync()
        {
            // Get the action (delete or edit) and professorId from the form
            var action = Request.Form["action"];
            var professorIdStr = Request.Form["professorId"];
            int professorId;

            // Check if the professorId is valid
            if (!int.TryParse(professorIdStr, out professorId))
            {
                _logger.LogWarning("Invalid professorId received.");
                return RedirectToPage("/viewEmployee");  // Redirect to the viewEmployee page if the professorId is invalid
            }
            if (action == "delete")
            {
                await DeleteEmployeeAsync(professorId);
                Message = "Employee profile deleted successfully!";
            }

            else if (action == "edit")
            {
                // Redirect to the Edit Employee page if 'edit' action is selected
                return RedirectToPage("/editEmployee", new { professorId = professorId });
            }
            else
            {
                _logger.LogWarning($"Unknown action: {action}");
            }

            // Return to the viewEmployee page after delete or edit action
            return RedirectToPage("/viewEmployee");
        }

        private async Task DeleteEmployeeAsync(int professorId)
        {
            try
            {
                // Connect to the database to delete the employee
                using SqlConnection conn = new(connString);
                await conn.OpenAsync();

                string query = "DELETE FROM dbo.userCICT WHERE professor_id = @ProfessorId";

                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@ProfessorId", professorId);
                    await cmd.ExecuteNonQueryAsync();
                }

                _logger.LogInformation($"Employee with ProfessorId {professorId} deleted.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error deleting employee with ProfessorId {professorId}: {ex.Message}");
                // Optionally, set a message for the view if the deletion fails
            }
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

                // If the user is admin, use "admin" as email for storage
                if (string.IsNullOrEmpty(userEmail) || User.IsInRole("Admin"))
                {
                    userEmail = "admin"; // Admin uses a static email 'admin' for profile picture
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

                return RedirectToPage("/dashboardAdmin");
            }
            catch (Exception ex)
            {
                Message = "Error uploading file: " + ex.Message;
                _logger.LogError("Error uploading profile picture: " + ex.Message);
                return Page();
            }
        }

        // Logout (Existing Code)
        public async Task<IActionResult> OnPostLogout()
        {
            await HttpContext.SignOutAsync();
            return RedirectToPage("/Login");
        }
    }
}
