using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity; // Import PasswordHasher
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Data.SqlClient;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration; // Add this using directive

namespace WebBookingApp.Pages
{
    public class LoginModel : PageModel
    {
        private readonly IConfiguration _configuration; // Add a field for IConfiguration

        [BindProperty]
        public string Email { get; set; }

        [BindProperty]
        public string Password { get; set; }

        public string Message { get; set; }

        // Inject IConfiguration through the constructor
        public LoginModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Retrieve the connection string from the configuration
            string connString = _configuration.GetConnectionString("DefaultConnection");
            string userType = null;
            string storedHashPassword = null;

            using (SqlConnection conn = new SqlConnection(connString))
            {
                conn.Open();

                var userQueries = new (string Table, string Column)[] {
                    ("userAdmin", "username"),
                    ("userAlumni", "email"),
                    ("userStudents", "email"),
                    ("userCICT", "email")
                };

                foreach (var (table, column) in userQueries)
                {
                    // Skip the admin check if the user is trying to log in as an admin
                    if (table == "userAdmin" && Email != "admin")
                    {
                        continue;
                    }

                    // Skip student/alumni checks if the user is trying to log in as an admin
                    if (table != "userAdmin" && Email == "admin")
                    {
                        continue;
                    }

                    string query = $"SELECT {column}, password FROM dbo.{table} WHERE {column} = @login";
                    using SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@login", Email);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            storedHashPassword = reader["password"].ToString(); // Get the password (hashed) from the DB
                            userType = table; // Identify the user type
                            break;
                        }
                    }
                }
            }

            // Check for admin login (admin doesn't have hashed password)
            if (userType == "userAdmin" && storedHashPassword != null)
            {
                if (Password == storedHashPassword) // Compare plain-text passwords for admin
                {
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Email, Email),
                        new Claim(ClaimTypes.Role, userType)
                    };

                    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var principal = new ClaimsPrincipal(identity);
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

                    return RedirectToPage("/dashboardAdmin");
                }
                else
                {
                    Message = "Invalid credentials.";
                    return Page();
                }
            }

            // Handle hashed passwords for other users (students, alumni, etc.)
            else if (userType != null && storedHashPassword != null)
            {
                // Use PasswordHasher to verify the hashed password
                var passwordHasher = new PasswordHasher<object>();
                var result = passwordHasher.VerifyHashedPassword(null, storedHashPassword, Password);

                if (result == PasswordVerificationResult.Success)
                {
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Email, Email),
                        new Claim(ClaimTypes.Role, userType)
                    };

                    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var principal = new ClaimsPrincipal(identity);
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

                    string redirectPage = userType switch
                    {
                        "userAdmin" => "/dashboardAdmin",
                        "userStudents" => "/dashboardStudent",
                        "userAlumni" => "/dashboardStudent",
                        "userCICT" => "/dashboardProfessor",
                        _ => "/dashboard"
                    };

                    return Redirect(redirectPage);
                }
                else
                {
                    Message = "Invalid credentials.";
                    return Page();
                }
            }

            Message = "Invalid credentials.";
            return Page();
        }
    }
}