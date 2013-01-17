//--------------------------------------------------------------------------
// 
//  Copyright (c) Microsoft Corporation.  All rights reserved. 
// 
//  File: TaskAsyncOperationContractGenerationExtension.cs
//
//--------------------------------------------------------------------------

using System;
using System.CodeDom;
using System.ComponentModel;
using System.Linq;
using System.ServiceModel.Description;
using System.Threading.Tasks;

namespace TaskWsdlImportExtension
{
    /// <summary>
    /// Called during contract generation that can be used to modify the generated code for an operation
    /// by adding a Task-based Asynchronous Pattern implementation.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public class TaskAsyncOperationContractGenerationExtension : IOperationContractGenerationExtension, IOperationBehavior
    {
        /// <summary>Generates the TAP implementation for a single operation.</summary>
        /// <param name="context">Information about the operation.</param>
        public void GenerateOperation(OperationContractGenerationContext context)
        {
            if (context.IsAsync)
            {
                string contractName = context.Contract.ContractType.Name;
                string clientTypeName = TaskAsyncWsdlImportExtension.DeriveClientTypeName(contractName);

                // Get the class to contain the new method.
                CodeTypeDeclaration clientClass = TaskAsyncWsdlImportExtension.FindClientType(clientTypeName, context.ServiceContractGenerator.TargetCompileUnit.Namespaces);

                // First, set up the new method, with attributes, name, parameters, and return type.
                CodeMemberMethod newTaskBasedMethod = new CodeMemberMethod()
                {
                    Attributes = MemberAttributes.Final | MemberAttributes.Public,
                    Name = context.SyncMethod.Name + "Async"
                };
                newTaskBasedMethod.Parameters.AddRange(context.SyncMethod.Parameters);
                bool returnsVoid = context.SyncMethod.ReturnType == null || context.SyncMethod.ReturnType.BaseType == "System.Void";
                if (returnsVoid)
                {
                    newTaskBasedMethod.ReturnType = new CodeTypeReference(typeof(Task));
                }
                else
                {
                    var returnType = new CodeTypeReference(typeof(Task<>));
                    returnType.TypeArguments.Add(context.EndMethod.ReturnType);
                    newTaskBasedMethod.ReturnType = returnType;
                }

                // Second, create the Task.Factory.FromAsync or Task<TResult>.Factory.FromAsync invoker.
                CodePropertyReferenceExpression getTaskFactory = new CodePropertyReferenceExpression();
                getTaskFactory.PropertyName = "Factory";
                CodeMethodInvokeExpression invokeFromAsync = new CodeMethodInvokeExpression();
                if (returnsVoid)
                {
                    getTaskFactory.TargetObject = new CodeTypeReferenceExpression(typeof(Task));
                }
                else
                {
                    var taskOfReturnType = new CodeTypeReference(typeof(Task<>));
                    taskOfReturnType.TypeArguments.Add(context.SyncMethod.ReturnType);
                    getTaskFactory.TargetObject = new CodeTypeReferenceExpression(taskOfReturnType);
                }
                invokeFromAsync.Method = new CodeMethodReferenceExpression(getTaskFactory, "FromAsync");
                newTaskBasedMethod.Statements.Add(new CodeMethodReturnStatement(invokeFromAsync));

                // Create the end delegate for the FromAsync call.
                var endDelegate = new CodeDelegateCreateExpression();
                endDelegate.MethodName = context.EndMethod.Name;
                endDelegate.TargetObject = new CodeCastExpression(contractName, new CodeThisReferenceExpression());
                if (returnsVoid)
                {
                    endDelegate.DelegateType = new CodeTypeReference(typeof(Action<IAsyncResult>));
                }
                else
                {
                    endDelegate.DelegateType = new CodeTypeReference(typeof(Func<,>));
                    endDelegate.DelegateType.TypeArguments.Add(typeof(IAsyncResult));
                    endDelegate.DelegateType.TypeArguments.Add(context.SyncMethod.ReturnType);
                }

                // If there are <= 3 parameters to the APM's Begin method, use a delegate-based
                // overload, as that's what TPL provides overloads for built-in.  If not,
                // use an overload that accepts an IAsyncResult as the first parameter.
                if (context.SyncMethod.Parameters.Count <= 3)
                {
                    // Create the begin delegate for the FromAsync call
                    // FromAsync(beginDelegate, endDelegate, null);
                    var beginDelegate = new CodeDelegateCreateExpression();
                    beginDelegate.MethodName = context.BeginMethod.Name;
                    beginDelegate.TargetObject = new CodeCastExpression(contractName, new CodeThisReferenceExpression());
                    switch (context.SyncMethod.Parameters.Count)
                    {
                        case 0: beginDelegate.DelegateType = new CodeTypeReference(typeof(Func<,,>)); break;
                        case 1: beginDelegate.DelegateType = new CodeTypeReference(typeof(Func<,,,>)); break;
                        case 2: beginDelegate.DelegateType = new CodeTypeReference(typeof(Func<,,,,>)); break;
                        case 3: beginDelegate.DelegateType = new CodeTypeReference(typeof(Func<,,,,,>)); break;
                    }
                    beginDelegate.DelegateType.TypeArguments.AddRange(context.SyncMethod.Parameters.Cast<CodeParameterDeclarationExpression>().Select(p => p.Type).ToArray());
                    beginDelegate.DelegateType.TypeArguments.Add(typeof(AsyncCallback));
                    beginDelegate.DelegateType.TypeArguments.Add(typeof(Object));
                    beginDelegate.DelegateType.TypeArguments.Add(typeof(IAsyncResult));

                    invokeFromAsync.Parameters.Add(beginDelegate);
                    invokeFromAsync.Parameters.Add(endDelegate);
                    invokeFromAsync.Parameters.AddRange((from parameter in context.SyncMethod.Parameters.Cast<CodeParameterDeclarationExpression>()
                                                         select new CodeVariableReferenceExpression(parameter.Name)).ToArray());
                    invokeFromAsync.Parameters.Add(new CodePrimitiveExpression(null));
                }
                else // > 3 parameters, so use the IAsyncResult overload
                {
                    // FromAsync(BeginMethod(inputParams, ..., asyncCallback, state), endDelegate)
                    var invokeBeginExpression = new CodeMethodInvokeExpression(
                        new CodeMethodReferenceExpression(new CodeCastExpression(contractName, new CodeThisReferenceExpression()), context.BeginMethod.Name));
                    invokeBeginExpression.Parameters.AddRange((from parameter in context.SyncMethod.Parameters.Cast<CodeParameterDeclarationExpression>()
                                                               select new CodeVariableReferenceExpression(parameter.Name)).ToArray());
                    invokeBeginExpression.Parameters.Add(new CodePrimitiveExpression(null)); // AsyncCallback
                    invokeBeginExpression.Parameters.Add(new CodePrimitiveExpression(null)); // state

                    invokeFromAsync.Parameters.Add(invokeBeginExpression);
                    invokeFromAsync.Parameters.Add(endDelegate);
                }

                // Finally, add the new method to the class
                clientClass.Members.Add(newTaskBasedMethod);
            }
        }

        #region Not Needed
        /// <summary>Not used.</summary>
        void IOperationBehavior.AddBindingParameters(OperationDescription operationDescription, System.ServiceModel.Channels.BindingParameterCollection bindingParameters) { }
        /// <summary>Not used.</summary>
        void IOperationBehavior.ApplyClientBehavior(OperationDescription operationDescription, System.ServiceModel.Dispatcher.ClientOperation clientOperation) { }
        /// <summary>Not used.</summary>
        void IOperationBehavior.ApplyDispatchBehavior(OperationDescription operationDescription, System.ServiceModel.Dispatcher.DispatchOperation dispatchOperation) { }
        /// <summary>Not used.</summary>
        void IOperationBehavior.Validate(OperationDescription operationDescription) { }
        #endregion
    }
}