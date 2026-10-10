using HikingStore.Enum;

namespace HikingStore.Areas.Admin.ViewModel
{
    public class Users_In_Role_ViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsLocked { get; set; }
        public bool EmailConfirmed { get; set; }
        public int OrderCount { get; set; }
        public List<string> Roles { get; set; } = new();
        public UserStatus Status { get; set; }
    }
}
