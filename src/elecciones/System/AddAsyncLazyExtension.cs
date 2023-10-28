using System.ComponentModel;
using NuGet.Common;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Provides the <see cref="AddAsyncLazy(IServiceCollection)"/> extension method 
    /// that allows resolving services lazily by taking a dependency on <see cref="AsyncLazy{T}"/>.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    static partial class AddAsyncLazyExtension
    {
        /// <summary>
        /// Allows resolving any service lazily using <see cref="Lazy{T}"/> as 
        /// a dependency instead of the direct service type.
        /// </summary>
        public static IServiceCollection AddAsyncLazy(this IServiceCollection services)
            => services.AddTransient(typeof(AsyncLazy<>), typeof(AsyncLazyService<>));

        class AsyncLazyService<T> : AsyncLazy<T> where T : class
        {
            public AsyncLazyService(IServiceProvider provider)
                : base(() => provider.GetRequiredService<Task<T>>()) { }
        }
    }
}