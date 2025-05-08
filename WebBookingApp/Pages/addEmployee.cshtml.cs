using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;
using Microsoft.AspNetCore.Identity;

namespace WebBookingApp.Pages
{
    public class addEmployeeModel : PageModel
    {
        private readonly ILogger<addEmployeeModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _connString;

        public addEmployeeModel(ILogger<addEmployeeModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _connString = _configuration.GetConnectionString("DefaultConnection");
        }

        public byte[]? ProfilePicture { get; private set; }

        [BindProperty] public IFormFile? UploadedFile { get; set; }
        public string Message { get; private set; } = "";

        [BindProperty] public string FirstName { get; set; }
        [BindProperty] public string LastName { get; set; }
        [BindProperty] public string MiddleName { get; set; }
        [BindProperty] public string ContactNumber { get; set; }
        [BindProperty] public string Gender { get; set; }
        [BindProperty] public string DateOfBirth { get; set; }
        [BindProperty] public string EmailAddress { get; set; }
        [BindProperty] public string Address { get; set; }
        [BindProperty] public string City { get; set; }
        [BindProperty] public string EmployeeStatus { get; set; }
        [BindProperty] public string EmploymentStatus { get; set; }
        [BindProperty] public string Program { get; set; }
        [BindProperty] public string JoiningDate { get; set; }
        [BindProperty] public string Password { get; set; }
        [BindProperty] public string ConfirmPassword { get; set; }

        public void OnGet()
        {
            _logger.LogInformation("OnGet() started");

            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;

            if (string.IsNullOrEmpty(userEmail) || User.IsInRole("Admin"))
            {
                userEmail = "admin";
            }

            using SqlConnection conn = new(_connString);
            conn.Open();

            string query = @"
                SELECT username 
                FROM dbo.userAdmin 
                WHERE username = @Username";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Username", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
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

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                Message = "Please ensure all required fields are filled out.";
                return Page();
            }

            if (Password != ConfirmPassword)
            {
                Message = "Passwords do not match.";
                return Page();
            }

            try
            {
                var hasher = new PasswordHasher<object>();
                string hashedPassword = hasher.HashPassword(null, Password);

                using SqlConnection conn = new(_connString);
                await conn.OpenAsync();

                string query = @"
                    INSERT INTO dbo.userCICT (
                        first_name, last_name, middle_name, contact_number,
                        gender, date_of_birth, email, address, city,
                        role, program, joining_date, password, employment_status
                    ) VALUES (
                        @FirstName, @LastName, @MiddleName, @ContactNumber,
                        @Gender, @DateOfBirth, @EmailAddress, @Address, @City,
                        @Role, @Program, @JoiningDate, @Password, @EmploymentStatus
                    );";

                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@FirstName", FirstName);
                    cmd.Parameters.AddWithValue("@LastName", LastName);
                    cmd.Parameters.AddWithValue("@MiddleName", MiddleName ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ContactNumber", ContactNumber);
                    cmd.Parameters.AddWithValue("@Gender", Gender);
                    cmd.Parameters.AddWithValue("@DateOfBirth", DateOfBirth);
                    cmd.Parameters.AddWithValue("@EmailAddress", EmailAddress);
                    cmd.Parameters.AddWithValue("@Address", Address);
                    cmd.Parameters.AddWithValue("@City", City);
                    cmd.Parameters.AddWithValue("@Role", EmployeeStatus);
                    cmd.Parameters.AddWithValue("@Program", Program);
                    cmd.Parameters.AddWithValue("@JoiningDate", JoiningDate);
                    cmd.Parameters.AddWithValue("@Password", hashedPassword);
                    cmd.Parameters.AddWithValue("@EmploymentStatus", EmploymentStatus);

                    await cmd.ExecuteNonQueryAsync();
                }

                Message = "Employee added successfully!";
                _logger.LogInformation("Employee data inserted successfully.");
                return Page();
            }
            catch (Exception ex)
            {
                Message = "An error occurred while adding the employee: " + ex.Message;
                _logger.LogError("Error inserting employee data: " + ex.Message);
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
