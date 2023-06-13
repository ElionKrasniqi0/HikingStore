using System.ComponentModel.DataAnnotations;

namespace HikingStore.Areas.Admin.ViewModel
{
    public class EditRoleNameViewModel
    {
        public EditRoleNameViewModel()
        {
            Users = new List<string>();
        }
        public string Id { get; set; }
        [Required(ErrorMessage = "Role Name is Required!")]
        public string RoleName { get; set; }
        public List<string> Users { get; set; }
    }
}
