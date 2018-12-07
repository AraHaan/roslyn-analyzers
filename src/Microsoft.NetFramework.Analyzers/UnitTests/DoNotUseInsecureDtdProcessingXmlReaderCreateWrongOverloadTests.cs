// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.CodeAnalysis.Testing;
using Test.Utilities;
using Xunit;

namespace Microsoft.NetFramework.Analyzers.UnitTests
{
    public partial class DoNotUseInsecureDtdProcessingAnalyzerTests : DiagnosticAnalyzerTestBase
    {
        private static readonly string s_CA3075XmlReaderCreateWrongOverloadMessage = MicrosoftNetFrameworkAnalyzersResources.XmlReaderCreateWrongOverloadMessage;

        private DiagnosticResult GetCA3075XmlReaderCreateWrongOverloadCSharpResultAt(int line, int column)
        {
            return GetCSharpResultAt(line, column, CA3075RuleId, s_CA3075XmlReaderCreateWrongOverloadMessage);
        }

        private DiagnosticResult GetCA3075XmlReaderCreateWrongOverloadBasicResultAt(int line, int column)
        {
            return GetBasicResultAt(line, column, CA3075RuleId, s_CA3075XmlReaderCreateWrongOverloadMessage);
        }

        [Fact]
        public void XmlReaderCreateWithoutXmlReaderSettingsShouldNotGenerateDiagnostic()
        {
            VerifyCSharp(@"
using System.Xml;

namespace TestNamespace
{
    class TestClass
    {
        private static void TestMethod()
        {
            var reader = XmlTextReader.Create(""doc.xml"");
        }
    }
}"
            );

            VerifyBasic(@"
Imports System.Xml

Namespace TestNamespace
    Class TestClass
        Private Shared Sub TestMethod()
            Dim reader = XmlTextReader.Create(""doc.xml"")
        End Sub
    End Class
End Namespace"
            );
        }

        [Fact]
        public void XmlReaderCreateWithoutXmlReaderSettingsInGetShouldNotGenerateDiagnostic()
        {
            VerifyCSharp(@"
using System.Xml;

class TestClass
{
    
    public XmlReader Test
    {
        get {
            XmlReader reader = XmlTextReader.Create(""doc.xml"");
            return reader;
        }
    }
}"
            );

            VerifyBasic(@"
Imports System.Xml

Class TestClass

    Public ReadOnly Property Test() As XmlReader
        Get
            Dim reader As XmlReader = XmlTextReader.Create(""doc.xml"")
            Return reader
        End Get
    End Property
End Class"
            );
        }

        [Fact]
        public void XmlReaderCreateWithoutXmlReaderSettingsInSetShouldNotGenerateDiagnostic()
        {
            VerifyCSharp(@"
using System.Xml;

class TestClass1
{
    XmlReader reader;
    public XmlReader Test
    {
        set
        {
            if (value == null)
                reader = XmlTextReader.Create(""doc.xml"");
            else
                reader = value;
        }
    }
}"
            );

            VerifyBasic(@"
Imports System.Xml

Class TestClass1
    Private reader As XmlReader
    Public WriteOnly Property Test() As XmlReader
        Set
            If value Is Nothing Then
                reader = XmlTextReader.Create(""doc.xml"")
            Else
                reader = value
            End If
        End Set
    End Property
End Class"
            );
        }

        [Fact]
        public void XmlReaderCreateWithoutXmlReaderSettingsInTryShouldNotGenerateDiagnostic()
        {
            VerifyCSharp(@"
using System;
using System.Xml;

class TestClass
{
    private void TestMethod()
    {
        try
        {
            var reader = XmlTextReader.Create(""doc.xml"");
        }
        catch (Exception) { throw; }
        finally { }
    }
}"
            );

            VerifyBasic(@"
Imports System
Imports System.Xml

Class TestClass
    Private Sub TestMethod()
        Try
            Dim reader = XmlTextReader.Create(""doc.xml"")
        Catch generatedExceptionName As Exception
            Throw
        Finally
        End Try
    End Sub
End Class"
            );
        }

        [Fact]
        public void XmlReaderCreateWithoutXmlReaderSettingsInCatchShouldNotGenerateDiagnostic()
        {
            VerifyCSharp(@"
using System;
using System.Xml;

class TestClass
{
    private void TestMethod()
    {
        try {        }
        catch (Exception) { 
            var reader = XmlTextReader.Create(""doc.xml"");
        }
        finally { }
    }
}"
            );

            VerifyBasic(@"
Imports System
Imports System.Xml

Class TestClass
    Private Sub TestMethod()
        Try
        Catch generatedExceptionName As Exception
            Dim reader = XmlTextReader.Create(""doc.xml"")
        Finally
        End Try
    End Sub
End Class"
            );
        }

        [Fact]
        public void XmlReaderCreateWithoutXmlReaderSettingsInFinallyShouldNotGenerateDiagnostic()
        {
            VerifyCSharp(@"
using System;
using System.Xml;

class TestClass
{
    private void TestMethod()
    {
        try {        }
        catch (Exception) { throw; }
        finally {
            var reader = XmlTextReader.Create(""doc.xml"");
        }
    }
}"
            );

            VerifyBasic(@"
Imports System
Imports System.Xml

Class TestClass
    Private Sub TestMethod()
        Try
        Catch generatedExceptionName As Exception
            Throw
        Finally
            Dim reader = XmlTextReader.Create(""doc.xml"")
        End Try
    End Sub
End Class"
            );
        }

        [Fact]
        public void XmlReaderCreateWithoutXmlReaderSettingsInAsyncAwaitShouldNotGenerateDiagnostic()
        {
            VerifyCSharp(@"
using System.Threading.Tasks;
using System.Xml;

class TestClass
{
    private async Task TestMethod()
    {
        await Task.Run(() => { var reader = XmlTextReader.Create(""doc.xml""); });
    }

    private async void TestMethod2()
    {
        await TestMethod();
    }
}"
            );

            VerifyBasic(@"
Imports System.Threading.Tasks
Imports System.Xml

Class TestClass
    Private Async Function TestMethod() As Task
        Await Task.Run(Function() 
        Dim reader = XmlTextReader.Create(""doc.xml"")

End Function)
    End Function

    Private Async Sub TestMethod2()
        Await TestMethod()
    End Sub
End Class"
            );
        }

        [Fact]
        public void XmlReaderCreateWithoutXmlReaderSettingsInDelegateShouldNotGenerateDiagnostic()
        {
            VerifyCSharp(@"
using System.Xml;

class TestClass
{
    delegate void Del();

    Del d = delegate () { var reader = XmlTextReader.Create(""doc.xml""); };
}"
            );

            VerifyBasic(@"
Imports System.Xml

Class TestClass
    Private Delegate Sub Del()

    Private d As Del = Sub() 
                            Dim reader = XmlTextReader.Create(""doc.xml"")
                       End Sub
End Class"
            );
        }

        [Fact]
        public void UseXmlReaderCreateTextReaderOnlyOverloadShouldNotGenerateDiagnostic()
        {
            VerifyCSharp(@"
using System.IO;
using System.Xml;

namespace TestNamespace
{
    class TestClass
    {
        private static void TestMethod()
        {
            var reader = XmlTextReader.Create(new StringReader(""<root> </root>""));
        }
    }
}"
            );
        }

        [Fact]
        public void UseXmlReaderCreateStreamOnlyOverloadShouldNotGenerateDiagnostic()
        {
            VerifyCSharp(@"
using System.IO;
using System.Text;
using System.Xml;

namespace TestNamespace
{
    class TestClass
    {
        private static void TestMethod()
        {
            var reader = XmlTextReader.Create(new MemoryStream(Encoding.UTF8.GetBytes(""<root> </root>"")));
        }
    }
}"
            );
        }
    }
}
