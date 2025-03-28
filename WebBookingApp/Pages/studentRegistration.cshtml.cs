using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.ComponentModel.DataAnnotations;
using System.Data.SqlClient;
using System.IO;
using System.Threading.Tasks;

namespace WebBookingApp.Pages
{
    public class studentRegistrationModel : PageModel
    {
        [BindProperty]
        public RegisterViewModel Input { get; set; }

        [BindProperty]
        public IFormFile IdPicture { get; set; } // File upload property

        private string connString = "Server=MELON\\SQL2022;Database=BOOKING_DB;Trusted_Connection=True;";



        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Validate if email or studentId is null or empty
            if (string.IsNullOrEmpty(Input.Email) || string.IsNullOrEmpty(Input.StudentId) && Input.Role == "Student")
            {
                ModelState.AddModelError(string.Empty, "Email and Student ID are required.");
                return Page();
            }

            // Convert Image to Byte Array
            byte[] imageBytes = null;
            if (IdPicture != null)
            {
                using (var memoryStream = new MemoryStream())
                {
                    await IdPicture.CopyToAsync(memoryStream);
                    imageBytes = memoryStream.ToArray();
                }
            }

            string tableName = Input.Role == "Alumni" ? "dbo.userAlumni" : "dbo.userStudents";
            bool isStudent = Input.Role == "Student"; // Check if registering as Student

            using (SqlConnection conn = new SqlConnection(connString))
            {
                conn.Open();

                // Check if email or student_id already exists to avoid duplicate entry
                string checkQuery = @"
        SELECT COUNT(*) FROM dbo.userVerification 
        WHERE email = @Email OR student_id = @StudentId";

                using (SqlCommand checkCmd = new SqlCommand(checkQuery, conn))
                {
                    checkCmd.Parameters.AddWithValue("@Email", Input.Email);
                    checkCmd.Parameters.AddWithValue("@StudentId", Input.StudentId ?? (object)DBNull.Value);

                    int existingRecords = (int)checkCmd.ExecuteScalar();

                    if (existingRecords > 0)
                    {
                        ModelState.AddModelError(string.Empty, "A user with this email or student ID already exists.");
                        return Page();
                    }
                }

                // Store registration in userVerification table for approval
                string verificationQuery = @"
        INSERT INTO dbo.userVerification 
        (email, student_id, first_name, middle_name, last_name, department, year_level, mobile_number, password, role, id_picture, status) 
        VALUES 
        (@Email, @StudentId, @FirstName, @MiddleName, @LastName, @Department, @YearLevel, @MobileNumber, @Password, @Role, @IdPicture, 'Pending')";

                using (SqlCommand verCmd = new SqlCommand(verificationQuery, conn))
                {
                    verCmd.Parameters.AddWithValue("@Email", Input.Email);
                    verCmd.Parameters.AddWithValue("@StudentId", Input.Role == "Alumni" ? DBNull.Value : (object)Input.StudentId); // Set StudentId to NULL for Alumni
                    verCmd.Parameters.AddWithValue("@FirstName", Input.FirstName);
                    verCmd.Parameters.AddWithValue("@MiddleName", (object?)Input.MiddleName ?? DBNull.Value);
                    verCmd.Parameters.AddWithValue("@LastName", Input.LastName);
                    verCmd.Parameters.AddWithValue("@Department", Input.Department);
                    verCmd.Parameters.AddWithValue("@YearLevel", isStudent ? (object)Input.YearLevel ?? DBNull.Value : DBNull.Value);
                    verCmd.Parameters.AddWithValue("@MobileNumber", Input.MobileNumber);
                    verCmd.Parameters.AddWithValue("@Password", Input.Password); // Hashing recommended
                    verCmd.Parameters.AddWithValue("@Role", Input.Role);
                    verCmd.Parameters.AddWithValue("@IdPicture", imageBytes ?? (object)DBNull.Value); // Store image if uploaded
                    verCmd.ExecuteNonQuery();
                }

                ViewData["ShowSuccessModal"] = true;
            }

            return Page();
        }


        public class RegisterViewModel
        {
            [Required]
            public string FirstName { get; set; }

            [Required]
            public string? MiddleName { get; set; }

            [Required]
            public string LastName { get; set; }

            [Required, EmailAddress]
            public string Email { get; set; }

            [Required]
            public string StudentId { get; set; }

            [Required]
            public string Department { get; set; }

            [Required, Phone]
            public string MobileNumber { get; set; }

            [Required, DataType(DataType.Password)]
            public string Password { get; set; }

            [Required, DataType(DataType.Password), Compare("Password")]
            public string ConfirmPassword { get; set; }

            [Required]
            public string Role { get; set; } // ✅ Determines if Student or Alumni

            public string? YearLevel { get; set; } // ✅ Optional for Students
        }
    }

}