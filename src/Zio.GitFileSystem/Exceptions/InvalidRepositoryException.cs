using System;
using System.Collections.Generic;
using System.Text;

namespace Zio.Exceptions
{
    internal class InvalidRepositoryException : Exception
    {
        public InvalidRepositoryException() : base() {}

        public InvalidRepositoryException(string message) : base(message) { }
    }
}
