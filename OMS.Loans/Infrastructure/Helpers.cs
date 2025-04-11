using DevExpress.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.Infrastructure
{
    public static class Helpers
    {
        public static List<string> GetPublicPropertyNames(this object obj)
        {
            return obj.GetType()
                      .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                      .Select(p => p.Name)
                      .ToList();
        }

        public static byte[] ReadLockedFileToByteArray(this IFileInfo fileInfo)
        {
            using var stream = fileInfo.Open(FileMode.Open);
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }

        public static void RemoveByReference<T>(this ObservableCollection<T> collection, T itemToRemove)
    where T : class
        {
            if (collection == null || itemToRemove == null)
                return;

            for (int i = 0; i < collection.Count; i++)
            {
                if (ReferenceEquals(collection[i], itemToRemove))
                {
                    collection.RemoveAt(i);
                    break;
                }
            }
        }

    }
}
