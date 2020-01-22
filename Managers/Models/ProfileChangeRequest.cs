using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using PhoneAttribute = Smartcontract.DataContracts.Attribute.PhoneAttribute;
namespace Smartcontract.App.Managers.Models
{
    public class ProfileChangeRequest
    {
        [Required]
        public string UniqueId{get;set;}        
        [Phone]
        public string Phone{get;set;}        
        [StringLength(50, ErrorMessage = "Город не должен превышать 50 символов")]
        public string City{get;set;}       
        [StringLength(150, ErrorMessage = "Адресс не должен превышать 150 символов")]
        public string Address{get;set;}   
        public IFormFile Avatar{get;set;}
    }
}
