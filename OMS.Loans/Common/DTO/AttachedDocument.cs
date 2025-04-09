using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevExpress.Mvvm;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.Common.DTO
{
    public partial class AttachedDocument:ObservableObject
    {
        public AttachedDocument()
        {
            
        }

        public string Name { get; set; }
        public string FullPath { get; set; }
        public byte[] Data { get; set; }

        public bool isInDb { get; set; } = false;

        [RelayCommand]
        public void Start() {

            if (FullPath.IsNullOrEmpty())
            {
                string tempFilePath = Path.Combine(Path.GetTempPath(),  Name);
                FullPath = tempFilePath;
                File.WriteAllBytes(tempFilePath, Data);
            }

            Process.Start(new ProcessStartInfo(FullPath)
            {
                UseShellExecute = true
            });
          

        }
        
    }
}
