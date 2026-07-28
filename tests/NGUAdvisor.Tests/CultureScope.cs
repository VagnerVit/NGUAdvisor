using System;
using System.Globalization;
using System.Threading;

namespace NGUAdvisor.Tests
{
    // Temporarily forces the current thread onto a given culture (e.g. "de-DE", which uses a comma decimal
    // separator and a period thousands separator) and restores the previous culture on Dispose. This is the
    // whole point of the SimpleJSON/ProfileModel suite: prove the JSON number round-trip is culture-invariant,
    // which is the class of corruption behind review findings #1 and #2. On an en-US dev machine those bugs
    // are invisible; running the same asserts inside a de-DE scope makes them reproducible.
    public sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _prevCulture;
        private readonly CultureInfo _prevUiCulture;

        public CultureScope(string cultureName)
        {
            _prevCulture = Thread.CurrentThread.CurrentCulture;
            _prevUiCulture = Thread.CurrentThread.CurrentUICulture;
            var c = CultureInfo.GetCultureInfo(cultureName);
            Thread.CurrentThread.CurrentCulture = c;
            Thread.CurrentThread.CurrentUICulture = c;
        }

        public void Dispose()
        {
            Thread.CurrentThread.CurrentCulture = _prevCulture;
            Thread.CurrentThread.CurrentUICulture = _prevUiCulture;
        }
    }
}
