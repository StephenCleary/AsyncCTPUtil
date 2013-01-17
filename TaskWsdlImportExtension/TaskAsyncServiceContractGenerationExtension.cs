//--------------------------------------------------------------------------
// 
//  Copyright (c) Microsoft Corporation.  All rights reserved. 
// 
//  File: TaskAsyncServiceContractGenerationExtension.cs
//
//--------------------------------------------------------------------------

using System.CodeDom;
using System.ComponentModel;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Description;

namespace TaskWsdlImportExtension
{
    /// <summary>Ensures that the client class to store our Task-based Asynchronous Methods is properly created.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public class TaskAsyncServiceContractGenerationExtension : IServiceContractGenerationExtension, IContractBehavior
    {
        /// <summary>Modifies the code document object model prior to the contract generation process.</summary>
        /// <param name="context">The code generated context to use to modify the code document prior to generation.</param>
        public void GenerateContract(ServiceContractGenerationContext context)
        {
            // Disable generation of the Event-Based Async Pattern, which has a conflicting naming scheme.
            context.ServiceContractGenerator.Options &= ~ServiceContractGenerationOptions.EventBasedAsynchronousMethods;

            string contractName = context.Contract.Name;
            string clientTypeName = TaskAsyncWsdlImportExtension.DeriveClientTypeName(contractName);

            // Look up the client class, and create it if it doesn't already exist.
            if (TaskAsyncWsdlImportExtension.FindClientType(clientTypeName, context.ServiceContractGenerator.TargetCompileUnit.Namespaces) == null)
            {
                // Create the new type
                CodeTypeDeclaration newClient = new CodeTypeDeclaration(clientTypeName)
                {
                    Attributes = MemberAttributes.Public,
                    IsPartial = true
                };
                newClient.BaseTypes.Add(new CodeTypeReference(typeof(ClientBase<>)) { TypeArguments = { new CodeTypeReference(contractName) } });
                newClient.BaseTypes.Add(new CodeTypeReference(contractName));

                // Add the new type to the right namespace
                CodeNamespace contractNamespace = (from ns in context.ServiceContractGenerator.TargetCompileUnit.Namespaces.Cast<CodeNamespace>()
                                                   from type in ns.Types.Cast<CodeTypeDeclaration>()
                                                   where type == context.ContractType
                                                   select ns).FirstOrDefault();
                contractNamespace.Types.Add(newClient);
            }
        }

        #region Not Needed
        /// <summary>Not used.</summary>
        void IContractBehavior.AddBindingParameters(ContractDescription contractDescription, ServiceEndpoint endpoint, System.ServiceModel.Channels.BindingParameterCollection bindingParameters) { }
        /// <summary>Not used.</summary>
        void IContractBehavior.ApplyClientBehavior(ContractDescription contractDescription, ServiceEndpoint endpoint, System.ServiceModel.Dispatcher.ClientRuntime clientRuntime) { }
        /// <summary>Not used.</summary>
        void IContractBehavior.ApplyDispatchBehavior(ContractDescription contractDescription, ServiceEndpoint endpoint, System.ServiceModel.Dispatcher.DispatchRuntime dispatchRuntime) { }
        /// <summary>Not used.</summary>
        void IContractBehavior.Validate(ContractDescription contractDescription, ServiceEndpoint endpoint) { }
        #endregion
    }
}
