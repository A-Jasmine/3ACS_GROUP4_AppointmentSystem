using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Data.SqlClient;

namespace WebBookingApp.Pages
{
    public class LoginModel : PageModel
    {
        [BindProperty]
        public string Email { get; set; }

        [BindProperty]
        public string Password { get; set; }

        public string Message { get; set; }

        public async Task<IActionResult> OnPostAsync()
        {
            string connString = "Server=MELON\\SQL2022;Database=BOOKING_DB;Trusted_Connection=True;";
            string userType = null;

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
                    // Admin login: must match 'admin' username
                    if (table == "userAdmin" && Email != "admin")
                    {
                        continue; // Skip checking admin table if input is not "admin"
                    }

                    // Student/Alumni/CICT login: skip if input is "admin"
                    if (table != "userAdmin" && Email == "admin")
                    {
                        continue;
                    }

                    // Check for the user based on table and column
                    string query = $"SELECT {column} FROM dbo.{table} WHERE {column} = @login AND password = @password";
                    using SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@login", Email); // Use the email for students, alumni, CICT, or the username for admin
                    cmd.Parameters.AddWithValue("@password", Password);

                    var result = cmd.ExecuteScalar();
                    if (result != null)
                    {
                        userType = table;
                        break;
                    }
                }
            }

            if (userType != null)
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

    }

}
