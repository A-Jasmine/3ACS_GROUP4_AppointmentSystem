using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.ComponentModel.DataAnnotations;
using System.Data.SqlClient;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace WebBookingApp.Pages
{
    public class studentRegistrationModel : PageModel
    {
        [BindProperty]
        public RegisterViewModel Input { get; set; }

        [BindProperty]
        public List<IFormFile> Images { get; set; } = new List<IFormFile>();

        private readonly string _connString;

        public studentRegistrationModel(IConfiguration configuration)
        {
            _connString = configuration.GetConnectionString("DefaultConnection");
        }


        public void OnGet() { }
        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
                return Page();

            // Validate phone number format
            if (!System.Text.RegularExpressions.Regex.IsMatch(Input.MobileNumber, @"^[0-9]{10,15}$"))
            {
                ModelState.AddModelError("Input.MobileNumber", "Phone number must be 10-15 digits");
                return Page();
            }
            // Prevent all zeros phone number
            else if (Input.MobileNumber.Trim('0') == "")
            {
                ModelState.AddModelError("Input.MobileNumber", "Phone number cannot be all zeros");
                return Page();
            }

            // Check password strength
            if (Input.Password.Length < 8)
            {
                ModelState.AddModelError("Input.Password", "Password must be at least 8 characters");
                return Page();
            }

            // Validate exactly 2 images
            if (Images.Count != 2)
            {
                ModelState.AddModelError("", "Please upload exactly 2 images");
                return Page();
            }

            var imageBytesList = new List<byte[]>();
            foreach (var image in Images)
            {
                if (image == null || image.Length == 0)
                {
                    ModelState.AddModelError("", "Both images are required");
                    return Page();
                }

                using var memoryStream = new MemoryStream();
                await image.CopyToAsync(memoryStream);
                imageBytesList.Add(memoryStream.ToArray());
            }

            byte[] firstImageBytes = imageBytesList.Count > 0 ? imageBytesList[0] : null;
            byte[] secondImageBytes = imageBytesList.Count > 1 ? imageBytesList[1] : null;

            {


                // Hash the password
                var passwordHasher = new PasswordHasher<RegisterViewModel>();
                var hashedPassword = passwordHasher.HashPassword(Input, Input.Password);

                using (SqlConnection conn = new SqlConnection(_connString))


                {
                    await conn.OpenAsync();

                    // Check if email exists in either userStudents or userAlumni
                    string checkEmailQuery = @"
                    SELECT COUNT(*) FROM (
                        SELECT email FROM dbo.userStudents WHERE email = @Email
                        UNION ALL
                        SELECT email FROM dbo.userAlumni WHERE email = @Email
                    ) AS CombinedEmails";

                    using (SqlCommand checkEmailCmd = new SqlCommand(checkEmailQuery, conn))
                    {
                        checkEmailCmd.Parameters.AddWithValue("@Email", Input.Email);
                        int emailCount = (int)await checkEmailCmd.ExecuteScalarAsync();
                        if (emailCount > 0)
                        {
                            ModelState.AddModelError("Input.Email", "Email already exists in our system");
                            return Page();
                        }
                    }

                    // Check if ID exists
                    string checkIdQuery = Input.Role == "Student"
                        ? "SELECT COUNT(*) FROM dbo.userStudents WHERE student_id = @Id"
                        : "SELECT COUNT(*) FROM dbo.userAlumni WHERE alumni_id = @Id";

                    using (SqlCommand checkIdCmd = new SqlCommand(checkIdQuery, conn))
                    {
                        checkIdCmd.Parameters.AddWithValue("@Id", Input.StudentId);
                        int idCount = (int)await checkIdCmd.ExecuteScalarAsync();
                        if (idCount > 0)
                        {
                            ModelState.AddModelError("Input.StudentId", "ID already exists in our system");
                            return Page();
                        }
                    }

                    // Check if phone exists
                    string checkPhoneQuery = @"
                    SELECT COUNT(*) FROM (
                        SELECT mobile_number FROM dbo.userStudents WHERE mobile_number = @Phone
                        UNION ALL
                        SELECT mobile_number FROM dbo.userAlumni WHERE mobile_number = @Phone
                    ) AS CombinedPhones";

                    using (SqlCommand checkPhoneCmd = new SqlCommand(checkPhoneQuery, conn))
                    {
                        checkPhoneCmd.Parameters.AddWithValue("@Phone", Input.MobileNumber);
                        int phoneCount = (int)await checkPhoneCmd.ExecuteScalarAsync();
                        if (phoneCount > 0)
                        {
                            ModelState.AddModelError("Input.MobileNumber", "Phone number already exists in our system");
                            return Page();
                        }
                    }

                    // Insert the new user
                    string insertQuery = @"
            INSERT INTO dbo.userVerification 
            (email, student_id, alumni_id, first_name, middle_name, last_name, program, year_level, 
             mobile_number, password, role, id_picture, id_picture2, status) 
            VALUES 
            (@Email, @StudentId, @AlumniId, @FirstName, @MiddleName, @LastName, @Program, @YearLevel, 
             @MobileNumber, @Password, @Role, @IdPicture, @IdPicture2, 'Pending')";

                    using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@Email", Input.Email);
                        cmd.Parameters.AddWithValue("@StudentId", Input.Role == "Student" ? (object)Input.StudentId : DBNull.Value);
                        cmd.Parameters.AddWithValue("@AlumniId", Input.Role == "Alumni" ? (object)Input.StudentId : DBNull.Value);
                        cmd.Parameters.AddWithValue("@FirstName", Input.FirstName);
                        cmd.Parameters.AddWithValue("@MiddleName", (object?)Input.MiddleName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@LastName", Input.LastName);
                        cmd.Parameters.AddWithValue("@Program", Input.Program);
                        cmd.Parameters.AddWithValue("@YearLevel", Input.Role == "Student" ? (object?)Input.YearLevel ?? DBNull.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@MobileNumber", Input.MobileNumber);
                        cmd.Parameters.AddWithValue("@Password", hashedPassword);
                        cmd.Parameters.AddWithValue("@Role", Input.Role);
                        cmd.Parameters.AddWithValue("@IdPicture", firstImageBytes);
                        cmd.Parameters.AddWithValue("@IdPicture2", secondImageBytes ?? (object)DBNull.Value);

                        await cmd.ExecuteNonQueryAsync();
                    }

                    ViewData["ShowSuccessModal"] = true;
                }

                return Page();
            }
        }
    

        public async Task<IActionResult> OnGetCheckEmailAsync(string email)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(_connString))

                {
                    await conn.OpenAsync();
                    string query = @"
                        SELECT COUNT(*) FROM (
                            SELECT email FROM dbo.userStudents WHERE email = @Email
                            UNION ALL
                            SELECT email FROM dbo.userAlumni WHERE email = @Email
                        ) AS CombinedEmails";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Email", email);
                        int count = (int)await cmd.ExecuteScalarAsync();
                        return new JsonResult(new { exists = count > 0 });
                    }
                }
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }

        public async Task<IActionResult> OnGetCheckIdAsync(string id, string role)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(_connString))

                {
                    await conn.OpenAsync();
                    string query = role == "Student"
                        ? "SELECT COUNT(*) FROM dbo.userStudents WHERE student_id = @Id"
                        : "SELECT COUNT(*) FROM dbo.userAlumni WHERE alumni_id = @Id";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Id", id);
                        int count = (int)await cmd.ExecuteScalarAsync();
                        return new JsonResult(new { exists = count > 0 });
                    }
                }
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }

        public async Task<IActionResult> OnGetCheckPhoneAsync(string phone)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(_connString))

                {
                    await conn.OpenAsync();
                    string query = @"
                        SELECT COUNT(*) FROM (
                            SELECT mobile_number FROM dbo.userStudents WHERE mobile_number = @Phone
                            UNION ALL
                            SELECT mobile_number FROM dbo.userAlumni WHERE mobile_number = @Phone
                        ) AS CombinedPhones";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Phone", phone);
                        int count = (int)await cmd.ExecuteScalarAsync();
                        return new JsonResult(new { exists = count > 0 });
                    }
                }
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }


        public class RegisterViewModel
        {
            [Required]
            public string FirstName { get; set; }

            public string? MiddleName { get; set; }

            [Required]
            public string LastName { get; set; }

            [Required, EmailAddress]
            public string Email { get; set; }

            [Required]
            public string StudentId { get; set; }

            [Required]
            public string Program { get; set; }

            [Required, Phone]
            public string MobileNumber { get; set; }

            [Required, DataType(DataType.Password)]
            public string Password { get; set; }

            [Required, DataType(DataType.Password), Compare("Password")]
            public string ConfirmPassword { get; set; }

            [Required]
            public string Role { get; set; }

            public string? YearLevel { get; set; }
        }
    }
}