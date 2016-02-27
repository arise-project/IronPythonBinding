// 
// CustomPythonWalker.cs
//  
// Author:
//       Carlos Alberto Cortez <calberto.cortez@gmail.com>
// 
// Copyright (c) 2012 Carlos Alberto Cortez
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;

using MonoDevelop.IronPython.Parser.Dom;

using IronPython.Compiler.Ast;
using ICSharpCode.NRefactory.TypeSystem;

namespace MonoDevelop.IronPython.Parser
{
	public class CustomPythonWalker : PythonWalker
	{
		PythonModule module;
		Stack<PythonContainerNode> containers;
		
		public CustomPythonWalker ()
		{
			module = new PythonModule ();
			containers = new Stack<PythonContainerNode> ();
			containers.Push (module);
		}
		
		public PythonModule Module {
			get { return module; }
		}
		
		public void Reset ()
		{
			module = new PythonModule ();
			containers.Clear ();
			containers.Push (module);
		}
		
		public override bool Walk (ImportStatement node)
		{
			var container = containers.Peek ();
			if (!container.SupportsImportStatements)
				return base.Walk (node);
			
			// import mod1, mod2, mod3,...
			for (int i = 0; i < node.Names.Count; i++) {
				var fullName = node.Names [i].MakeString ();
				var asname = node.AsNames [i];
				
				var import = new PythonImport () {
					ModuleName = fullName,
					AsName = asname,
					Region = GetDomRegion (node)
				};
				
				container.Imports.Add (import);
			}
			
			return base.Walk (node);
		}
		
		public override bool Walk (FromImportStatement node)
		{
			var container = containers.Peek ();
			if (!container.SupportsFromStatements)
				return base.Walk (node);
			
			var fromImport = new PythonFromImport () {
				ModuleName = node.Root.MakeString (),
				Region = GetDomRegion (node)
			};
			
			// even if '*' is present, we may get an alias for a member
			// (using 'as')
			bool importAll = node.Names.Where (name => name == "*").Count () > 0;
			
			var names = new List<PythonNameReference> ();
			for (int i = 0; i < node.Names.Count; i++) {
				var name = node.Names [i];
				var asname = node.AsNames == null ? null : node.AsNames [i];
				
				// Discard unused data
				if (importAll && (String.IsNullOrEmpty (asname) || name == "*"))
					continue;
				
				var nameRef = new PythonNameReference () {
					Name = node.Names [i],
					AsName = node.AsNames == null ? null : node.AsNames [i]
				};
				names.Add (nameRef);
			}
			
			fromImport.Names = names;
			fromImport.ImportAll = importAll;
			
			container.FromImports.Add (fromImport);
			
			return base.Walk (node);
		}
		
		public override bool Walk (ClassDefinition node)
		{
			var klass = new PythonClass () {
				Name = node.Name,
				Region = GetDomRegion (node),
				Documentation = node.Documentation
			};
			
			containers.Peek ().Classes.Add (klass);
			containers.Push (klass);
			
			return base.Walk (node);
		}

		public override bool Walk (FunctionDefinition node)
		{
			if (node.IsLambda)
				return base.Walk (node);
			
			var function = new PythonFunction () {
				Name = node.Name,
				Region = GetDomRegion (node),
				Documentation = node.Documentation
			};
			
			var parameters = node.Parameters;
			for (int i = 0; i < parameters.Count; i++) {
				var param = parameters [i];
				
				string name = param.Name;
				if (param.IsList)
					name = "*" + name;
				else if (param.IsDictionary)
					name = "**" + name;
				
				var p = new PythonArgument () {
					Name = name,
					Position = i
				};
				
				function.Arguments.Add (p);
			}
			
			containers.Peek ().Functions.Add (function);
			containers.Push (function);
			
			return base.Walk (node);
		}
		
		// Need an explanation/note here.
		public override bool Walk (AssignmentStatement node)
		{
			var container = containers.Peek ();
			
			foreach (Expression expr in node.Left) {
				switch (expr.NodeName) {
				case "NameExpression":
					string identifier = ((NameExpression)expr).Name;
					if (container is PythonFunction)
						((PythonFunction)container).Locals.Add (new PythonLocal () {
							Name = identifier,
							Region = GetDomRegion (node)
						});
					else
						container.Attributes.Add (new PythonAttribute () {
							Name = identifier,
							Type = PythonAttributeType.Static,
							Region = GetDomRegion (node)
						});
					
					break;
				case "MemberExpression":
					string attrIdentifier;
					if (!(container is PythonFunction) ||
						!IsSelfAttr ((PythonFunction)container, (MemberExpression)expr, out attrIdentifier))
						continue;
					
					var parent = FindParentClass ();
					if (parent != null)
						parent.Attributes.Add (new PythonAttribute () {
							Name = attrIdentifier,
							Type = PythonAttributeType.Instance,
							Region = GetDomRegion (expr)
						});
					
					break;
				}
			}
			
			return base.Walk (node);
		}
		
		public override void PostWalk (ClassDefinition node)
		{
			containers.Pop ();
		}
		
		public override void PostWalk (FunctionDefinition node)
		{
			if (!node.IsLambda)
				containers.Pop ();
		}
		
		PythonClass FindParentClass ()
		{
			// Walk up the stack until we found a class, or null if none
			// (taking advantage of the reverse enumerator).
			foreach (var container in containers)
				if (container is PythonClass)
					return (PythonClass)container;
			
			return null;
		}
		
		// We only try to add simple member expr, such "self.something",
		// which are most likely all the possibilites to declare/initialize
		// a class attribute (field).
		static bool IsSelfAttr (PythonFunction func, MemberExpression expr, out string attrName)
		{
			attrName = null;
			
			// NameExpression -> Simple name access
			var target = expr.Target as NameExpression;
			if (target == null)
				return false;
			
			if (func.Arguments.Count == 0 ||
				func.Arguments [0].Name != target.Name)
				return false;
			
			attrName = expr.Name; // self.Name
			return true;
		}

		static DomRegion GetDomRegion (Node node)
		{
			return new DomRegion (node.Start.Line, node.End.Line);
		}
	}
}

