using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Smartcontract.App.Models
{
    public class CustomFile
    {
        public byte[] FileContents { get; set; }
        public string ContentType { get; set; }
        public string FileName { get; set; }
    }
}
