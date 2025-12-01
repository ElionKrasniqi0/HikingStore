using Microsoft.AspNetCore.Mvc.Rendering;

namespace HikingStore.Areas.Admin.ViewModel
{
    public class EditRoleViewModel
    {
        public string UserId { get; set; }
        public string RoleId { get; set; }
        public SelectListItem Roles { get; set; }
        public List<string> UserRoles { get; set; }
    }
}
