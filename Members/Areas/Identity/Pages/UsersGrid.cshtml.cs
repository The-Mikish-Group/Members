using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Members.Areas.Identity.Pages
{
    // Using the primary constructor for dependency injection
    public class UsersGridModel
    {
        public class UserModel
        {
            public string? Id { get; set; }
            public string? FullName { get; set; }
            public string? Email { get; set; }
            public bool EmailConfirmed { get; set; }
            public string? PhoneNumber { get; set; }
            public bool PhoneNumberConfirmed { get; set; }
            public string? FirstName { get; set; }
            public string? MiddleName { get; set; }
            public string? LastName { get; set; }
            public string? AddressLine1 { get; set; }
            public string? AddressLine2 { get; set; }
            public string? City { get; set; }
            public string? State { get; set; }
            public string? ZipCode { get; set; } // Added this property
            public string? Plot { get; set; }
            public DateTime? Birthday { get; set; }
            public DateTime? Anniversary { get; set; }
            public List<string>? Roles { get; set; }
            public DateTime? LastLogin { get; set; }
        }

        public List<UserModel>? Users { get; set; }
        public int TotalUsers { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public string? SortColumn { get; set; }
        public string? SortOrder { get; set; }
        public string? SearchTerm { get; set; }
        public int TotalPages { get; set; }
    }

    // This helper class is needed for combining multiple LINQ Where clauses with OR
    // You might need to add a reference to the LinqKit NuGet package
    // or implement this class yourself.
    // If you prefer not to add LinqKit, you would need to construct the
    // filter expression differently or chain .Where() calls if applicable.
    // For complex OR conditions across relationships, PredicateBuilder is helpful.
    //public static class PredicateBuilder
    //{
    //    public static System.Linq.Expressions.Expression<Func<T, bool>> True<T>() { return f => true; }
    //    public static System.Linq.Expressions.Expression<Func<T, bool>> False<T>() { return f => false; }

    //    public static System.Linq.Expressions.Expression<Func<T, bool>> Or<T>(this System.Linq.Expressions.Expression<Func<T, bool>> expr1,
    //                                                                         System.Linq.Expressions.Expression<Func<T, bool>> expr2)
    //    {
    //        var invokedExpr = System.Linq.Expressions.Expression.Invoke(expr2, expr1.Parameters.Cast<System.Linq.Expressions.Expression>());
    //        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(System.Linq.Expressions.Expression.OrElse(expr1.Body, invokedExpr), expr1.Parameters);
    //    }

    //    public static System.Linq.Expressions.Expression<Func<T, bool>> And<T>(this System.Linq.Expressions.Expression<Func<T, bool>> expr1,
    //                                                                         System.Linq.Expressions.Expression<Func<T, bool>> expr2)
    //    {
    //        var invokedExpr = System.Linq.Expressions.Expression.Invoke(expr2, expr1.Parameters.Cast<System.Linq.Expressions.Expression>());
    //        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(System.Linq.Expressions.Expression.AndAlso(expr1.Body, invokedExpr), expr1.Parameters);
    //    }
    //}
}