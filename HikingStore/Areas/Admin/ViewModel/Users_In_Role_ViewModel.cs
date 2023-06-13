using System.Collections.Generic;
using HikingStore.Enum;

namespace HikingStore.Areas.Admin.ViewModel
{
    public class Users_In_Role_ViewModel
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
        public bool IsLocked { get; set; }
        public List<string> Roles { get; set; }
        public UserStatus Status { get; set; }
    }
}
