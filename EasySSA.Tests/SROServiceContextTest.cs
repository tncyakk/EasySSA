using EasySSA.Component;
using EasySSA.Common;
// <copyright file="SROServiceContextTest.cs">Copyright ©  2017</copyright>

using System;
using EasySSA.Context;
using Microsoft.Pex.Framework;
using Microsoft.Pex.Framework.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EasySSA.Context.Tests
{
    [TestClass]
    [PexClass(typeof(SROServiceContext))]
    [PexAllowedExceptionFromTypeUnderTest(typeof(ArgumentException), AcceptExceptionSubtypes = true)]
    [PexAllowedExceptionFromTypeUnderTest(typeof(InvalidOperationException))]
    public partial class SROServiceContextTest
    {

        [PexMethod]
        [PexAllowedException(typeof(NullReferenceException))]
        public SROServiceContext Constructor(Client client, SROServiceComponent serviceComponent) {
            SROServiceContext target = new SROServiceContext(client, serviceComponent);
            return target;
            // TODO: add assertions to method SROServiceContextTest.Constructor(Client, SROServiceComponent)
        }
    }
}
