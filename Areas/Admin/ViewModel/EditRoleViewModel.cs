namespace HikingStore.Areas.Admin.ViewModel
{
    public class EditRoleViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string? RoleId { get; set; }
        public List<string> UserRoles { get; set; } = new();
    }
}
