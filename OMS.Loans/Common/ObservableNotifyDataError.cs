using CommunityToolkit.Mvvm.ComponentModel;
using OMS.Loans.ViewModels;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.Common
{
    public abstract class ObservableNotifyDataError : ObservableObject, INotifyDataErrorInfo
    {

        private readonly Dictionary<string, List<string>> _errors = new();

        public bool HasErrors => _errors.Count > 0;

        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        protected void OnErrorsChanged(string propertyName)
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        }

        public IEnumerable GetErrors(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
                return null;

            return _errors.TryGetValue(propertyName, out var errors) ? errors : null;
        }

        protected void AddError(string propertyName, string error)
        {
            if (!_errors.ContainsKey(propertyName))
                _errors[propertyName] = new List<string>();

            if (!_errors[propertyName].Contains(error))
            {
                _errors[propertyName].Add(error);
                OnErrorsChanged(propertyName);
            }
        }

        protected void ClearErrors(string propertyName)
        {
            if (_errors.Remove(propertyName))
            {
                OnErrorsChanged(propertyName);
            }
        }


        public T ValidateProp<T>(T item, Expression<Func<T, object>> propertySelector, Func<T, bool> predicate, string errorMessage)
        {
            var propName = GetPropertyName(propertySelector);
            this.ClearErrors(propName);

            if (predicate(item))
            {
                this.AddError(propName, errorMessage);
            }

            return item;
        }

        private static string GetPropertyName<T>(Expression<Func<T, object>> expression)
        {
            if (expression.Body is MemberExpression member)
                return member.Member.Name;

            if (expression.Body is UnaryExpression unary && unary.Operand is MemberExpression memberExpr)
                return memberExpr.Member.Name;

            throw new ArgumentException("Invalid property expression");
        }

        private  List<string> GetPublicPropertyNames()
        {
            return this.GetType()
                      .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                      .Select(p => p.Name)
                      .ToList();
        }

        public void ValidateAllProperties()
        {
           var props= GetPublicPropertyNames();
            if (props.Count == 0) {
                return;
            }

            for (int i = 0; i < props.Count; i++)
            {
                OnPropertyChanged(props[i]);
            }    
            
        }
    }

   
}
