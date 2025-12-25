using System.Collections.Generic;
using WedNightFury.Models;

namespace WedNightFury.Models.ViewModels
{
    public class ServiceDetailsVm
    {
        public Service Service { get; set; } = new Service();
        public List<Service> Related { get; set; } = new List<Service>();
    }
}
