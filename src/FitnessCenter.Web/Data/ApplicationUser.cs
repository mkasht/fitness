using Microsoft.AspNetCore.Identity;

namespace FitnessCenter.Web.Data;

public class ApplicationUser : IdentityUser
{
    [PersonalData]
    public string FullName { get; set; } = "";
}

