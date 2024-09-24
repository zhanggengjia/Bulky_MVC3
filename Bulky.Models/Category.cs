using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bulky.Models
{
    public class Category
    {
        [Key]
        public int Id { get; set; }
        [Required(ErrorMessage ="Can't be empty")]
        [Display(Name="Category Name")]
        [MaxLength(30, ErrorMessage ="less than 30 words")]
        public string Name { get; set; }
        [DisplayName("Display Order")]
        [Required(ErrorMessage ="Can't be empty")]
        [Range(1,100, ErrorMessage ="1-100")]
        public int DisplayOrder {  get; set; }
    }
}
