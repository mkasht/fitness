using System.ComponentModel.DataAnnotations;

namespace FitnessCenter.Web.Data;

public sealed class SiteText
{
    [Key, MaxLength(100)] public string Key { get; set; } = "";
    [MaxLength(2000)] public string Value { get; set; } = "";
}
