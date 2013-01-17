//--------------------------------------------------------------------------
// 
//  Copyright (c) Microsoft Corporation.  All rights reserved. 
// 
//  File: TaskAsyncWsdlImportExtension.cs
//
//--------------------------------------------------------------------------

using System.CodeDom;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.ServiceModel.Description;
using System.Web.Services.Description;
using System.Xml;
using System.Xml.Schema;

namespace TaskWsdlImportExtension
{
    /// <summary>Simple WSDL import extension for the Task-based Asynchronous Pattern.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public class TaskAsyncWsdlImportExtension : IWsdlImportExtension
    {
        /// <summary>Called when importing a contract.</summary>
        /// <param name="importer">The importer.</param>
        /// <param name="context">The import context to be modified.</param>
        public void ImportContract(WsdlImporter importer, WsdlContractConversionContext context)
        {
            // Ensure that the client class has been appropriately created in order for us to add methods to it.
            context.Contract.Behaviors.Add(new TaskAsyncServiceContractGenerationExtension());

            // For each operation, add a task-based async equivalent.
            foreach (Operation operation in context.WsdlPortType.Operations)
            {
                var description = context.Contract.Operations.Find(operation.Name);
                if (description != null)
                    description.Behaviors.Add(new TaskAsyncOperationContractGenerationExtension());
            }
        }

        #region Not Needed
        /// <summary>Not used.</summary>
        public void BeforeImport(ServiceDescriptionCollection wsdlDocuments, XmlSchemaSet xmlSchemas, ICollection<XmlElement> policy) {}
        /// <summary>Not used.</summary>
        public void ImportEndpoint(WsdlImporter importer, WsdlEndpointConversionContext context) { }
        #endregion

        /// <summary>Derives the client type name from the interface name.</summary>
        /// <param name="interfaceName">The name of the service interface.</param>
        /// <returns>The computed name of the client type.</returns>
        internal static string DeriveClientTypeName(string interfaceName)
        {
            return ((interfaceName.StartsWith("I") && interfaceName.Length >= 2) ?
                interfaceName.Substring(1) : interfaceName) + "Client";
        }

        /// <summary>Finds the client type by name in the provided collection of namespaces.</summary>
        /// <param name="typeName">The name of the client type for which to search.</param>
        /// <param name="namespaces">The collection of namespaces to search.</param>
        /// <returns>The client type if found; otherwise, null.</returns>
        internal static CodeTypeDeclaration FindClientType(string typeName, CodeNamespaceCollection namespaces)
        {
            return (from ns in namespaces.Cast<CodeNamespace>()
                    from type in ns.Types.Cast<CodeTypeDeclaration>()
                    where type.Name == typeName
                    select type).FirstOrDefault();
        }
    }
}