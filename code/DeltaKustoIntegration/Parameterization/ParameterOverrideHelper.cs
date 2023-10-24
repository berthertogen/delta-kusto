﻿using DeltaKustoLib;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace DeltaKustoIntegration.Parameterization
{
    public static class ParameterOverrideHelper
    {
        //  This is used instead of reflection since this isn't easily supported
        //  with self-contained executable
        private readonly static IDictionary<
            Type,
            Action<object, IImmutableStack<PathComponent>, string>> _inplaceOverrideMap =
            CreateInplaceOverrideMap();
        private readonly static IDictionary<
            Type,
            Func<object>> _newInstanceMap =
            CreateNewInstanceMap();

        #region Inner Types
        private class PathComponent
        {
            public PathComponent(string property, int? index = null)
            {
                Property = property;
                Index = index;
            }

            public string Property { get; }

            public int? Index { get; }

            public override string ToString()
            {
                return Index == null
                    ? Property
                    : $"{Property}[{Index}]";
            }
        }
        #endregion

        public static void InplaceOverride(object target, params string[] pathOverrides)
        {
            InplaceOverride(target, (IEnumerable<string>)pathOverrides);
        }

        public static void InplaceOverride(object target, IEnumerable<string> pathOverrides)
        {
            if (pathOverrides.Any())
            {
                try
                {
                    var splits = pathOverrides.Select(t => t.Split('=', 2));
                    var noEquals = splits.FirstOrDefault(s => s.Length != 2);

                    if (noEquals != null)
                    {
                        throw new DeltaException(
                            $"Override must be of the form path=value ; "
                            + $"exception:  '{string.Join('=', noEquals)}'");
                    }

                    var overrides = splits.Select(s => (path: s[0], textValue: s[1]));

                    InplaceOverride(target, overrides);
                }
                catch (Exception ex)
                {
                    throw new DeltaException(
                        $"Issue with the following parameter override:  '{pathOverrides}'",
                        ex);
                }
            }
        }

        public static void InplaceOverride(
            object target,
            IEnumerable<(string path, string textValue)> overrides)
        {
            if (overrides is null)
            {
                throw new ArgumentNullException(nameof(overrides));
            }

            foreach (var o in overrides)
            {
                InplaceOverride(target, o.path, o.textValue);
            }
        }

        public static void InplaceOverride(object target, string path, string textValue)
        {
            try
            {
                var components = ParsePath(path);

                RecursiveInplaceOverride(target, components, textValue);
            }
            catch (DeltaException ex)
            {
                throw new DeltaException($"Issue with override property path '{path}'", ex);
            }
        }

        [UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode")]
        private static void RecursiveInplaceOverride(
            object target,
            IImmutableStack<PathComponent> components,
            string textValue)
        {   //  Determine if target is a dictionary or object
            var isDictionary = target.GetType().IsGenericType
                && target.GetType().GetGenericTypeDefinition() == typeof(Dictionary<,>);

            if (isDictionary)
            {
                var arguments = target.GetType().GetGenericArguments();
                var keyType = arguments[0];
                var valueType = arguments[1];
                var method = _inplaceOverrideMap[valueType];

                method(target, components, textValue);
            }
            else
            {
                RecursiveInplaceOverrideOnObject(target, components, textValue);
            }
        }

        private static IDictionary<Type, Action<object, IImmutableStack<PathComponent>, string>>
            CreateInplaceOverrideMap()
        {
            var builder =
                ImmutableDictionary<Type, Action<object, IImmutableStack<PathComponent>, string>>
                .Empty
                .ToBuilder();

            builder.Add(
                typeof(JobParameterization),
                RecursiveInplaceOverrideOnDictionaryRouter<JobParameterization>);
            builder.Add(
                typeof(TokenParameterization),
                RecursiveInplaceOverrideOnDictionaryRouter<TokenParameterization>);

            var map = builder.ToImmutableDictionary();

#if DEBUG
            ValidateInplaceOverrideMap(map, typeof(MainParameterization));
#endif

            return map;
        }

#if DEBUG
        private static void ValidateInplaceOverrideMap(
            IImmutableDictionary<Type, Action<object, IImmutableStack<PathComponent>, string>> map,
            Type type)
        {
            foreach (var prop in type.GetProperties())
            {
                var isDictionary = prop.PropertyType.IsGenericType
                    && prop.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>);

                if (isDictionary)
                {
                    var arguments = prop.PropertyType.GetGenericArguments();
                    var keyType = arguments[0];
                    var valueType = arguments[1];

                    if (keyType != typeof(string))
                    {
                        throw new NotSupportedException("We only support string-keyed map");
                    }
                    if (!map.ContainsKey(valueType))
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(map),
                            $"Missing key '{valueType.Name}'");
                    }
                }
                //  Recursive validation
                ValidateInplaceOverrideMap(map, prop.PropertyType);
            }
        }
#endif

        private static void RecursiveInplaceOverrideOnDictionaryRouter<T>(
            object target,
            IImmutableStack<PathComponent> components,
            string textValue) where T : class, new()
        {
            RecursiveInplaceOverrideOnDictionary(
                (IDictionary<string, T>)target,
                components,
                textValue);
        }

        private static void RecursiveInplaceOverrideOnDictionary<T>(
            IDictionary<string, T> target,
            IImmutableStack<PathComponent> components,
            string textValue) where T : class, new()
        {
            var component = components.Peek();
            var remainingProperties = components.Pop();

            if (remainingProperties.IsEmpty)
            {
                throw new DeltaException($"Can't override a dictionary at '{component.Property}'");
            }
            if (component.Index != null)
            {
                throw new DeltaException(
                    $"Dictionary can't be accessed with "
                    + $"an index at '{component.Property}'");
            }
            //  If the key doesn't exist in the dictionary, we create it
            if (!target.ContainsKey(component.Property))
            {
                target[component.Property] = new T();
            }

            var newTarget = target[component.Property];

            if (newTarget == null)
            {
                throw new DeltaException($"Property '{component.Property}' is null");
            }

            RecursiveInplaceOverride(
                newTarget,
                remainingProperties,
                textValue);
        }

        private static IDictionary<Type, Func<object>> CreateNewInstanceMap()
        {
            var builder =
                ImmutableDictionary<Type, Func<object>>
                .Empty
                .ToBuilder();

            builder.Add(
                typeof(TokenProviderParameterization),
                () => new TokenProviderParameterization());
            builder.Add(
                typeof(ServicePrincipalLoginParameterization),
                () => new ServicePrincipalLoginParameterization());
            builder.Add(
                typeof(UserPromptParameterization),
                () => new UserPromptParameterization());
            builder.Add(
                typeof(AzCliParameterization),
                () => new AzCliParameterization());
            builder.Add(
                typeof(UserManagedIdentityParameterization),
                () => new UserManagedIdentityParameterization());
            builder.Add(
                typeof(SourceParameterization),
                () => new SourceParameterization());
            builder.Add(
                typeof(AdxSourceParameterization),
                () => new AdxSourceParameterization());
            builder.Add(
                typeof(SourceFileParametrization),
                () => new SourceFileParametrization());
            builder.Add(
                typeof(ActionParameterization),
                () => new ActionParameterization());
            builder.Add(
                typeof(Dictionary<string, JobParameterization>),
                () => new Dictionary<string, JobParameterization>());
            builder.Add(
                typeof(Dictionary<string, TokenParameterization>),
                () => new Dictionary<string, TokenParameterization>());

            var map = builder.ToImmutableDictionary();

#if DEBUG
            ValidateNewInstanceMap(map, typeof(MainParameterization));
#endif

            return map;
        }

#if DEBUG
        private static void ValidateNewInstanceMap(
            ImmutableDictionary<Type, Func<object>> map,
            Type type)
        {
            foreach (var prop in type.GetProperties())
            {   //  Excluse string, bool, etc.
                if (prop.PropertyType.Namespace != "System")
                {
                    var isDictionary = prop.PropertyType.IsGenericType
                        && prop.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>);

                    if (!map.ContainsKey(prop.PropertyType))
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(map),
                            $"Missing key '{prop.PropertyType.Name}'");
                    }
                    if (!isDictionary)
                    {   //  Recursive validation
                        ValidateNewInstanceMap(map, prop.PropertyType);
                    }
                }
            }
        }
#endif

        private static void RecursiveInplaceOverrideOnObject(
            object target,
            IImmutableStack<PathComponent> components,
            string textValue)
        {
            var component = components.Peek();
            var property = component.Property;
            var realProperty = GetRealProperty(property);
            var propertyInfo = target.GetType().GetProperty(realProperty);
            var remainingProperties = components.Pop();

            if (propertyInfo == null)
            {
                throw new DeltaException($"Property '{property}' doesn't exist on object");
            }

            if (!remainingProperties.IsEmpty)
            {
                var newTarget = propertyInfo.GetGetMethod()!.Invoke(target, new object[0]);

                if (newTarget == null)
                {   //  Property is null, we try to create it
                    newTarget = _newInstanceMap[propertyInfo.PropertyType]();

                    propertyInfo.GetSetMethod()!.Invoke(target, new[] { newTarget });
                }

                if (component.Index != null)
                {
                    var index = component.Index.Value;
                    var array = newTarget as object[];

                    if (array == null)
                    {
                        throw new DeltaException(
                            $"Property '{property}' can't be accessed by index");
                    }
                    if (array.Length <= component.Index)
                    {
                        throw new DeltaException(
                            $"Property '{property}' index '{index}' is out of bound");
                    }

                    newTarget = array[index];
                }

                RecursiveInplaceOverride(
                    newTarget,
                    remainingProperties,
                    textValue);
            }
            else
            {
                var value = ParseValue(textValue, propertyInfo.PropertyType);

                propertyInfo.GetSetMethod()!.Invoke(target, new object[] { value });
            }
        }

        [UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode")]
        private static object ParseValue(string textValue, Type type)
        {
            if (type == typeof(string))
            {
                return textValue;
            }
            else
            {
                try
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    var value = JsonSerializer.Deserialize(textValue, type, options);

                    if (value == null)
                    {
                        throw new DeltaException($"Following override leads to no value:  '{textValue}'");
                    }

                    return value;
                }
                catch (Exception ex)
                {
                    throw new DeltaException(
                        $"Issue deserializing override into expected type:  '{textValue}'",
                        ex);
                }
            }
        }

        private static string GetRealProperty(string pathPropertyName)
        {
            var property = char.ToUpper(pathPropertyName[0]) + pathPropertyName.Substring(1);

            return property;
        }

        private static IImmutableStack<PathComponent> ParsePath(string path)
        {
            var components = path
                .Split('.')
                .Select(p => ParseComponent(p))
                .Reverse();

            //  Create a stack to efficiently recurse over the properties
            var stack = ImmutableStack<PathComponent>.Empty;

            foreach (var c in components)
            {
                stack = stack.Push(c);
            }

            return stack;
        }

        private static PathComponent ParseComponent(string componentText)
        {
            if (componentText.Length == 0)
            {
                throw new DeltaException("Empty property within property path");
            }

            var illegalCharacter = componentText
                .Where(c => !(char.IsLetter(c) || char.IsDigit(c) || c != '_'))
                .FirstOrDefault();

            if (illegalCharacter != default(char))
            {
                throw new DeltaException(
                    $"Illegal character '{illegalCharacter}' in property path '{componentText}'");
            }

            var bracketOpenIndex = componentText.IndexOf('[');

            if (bracketOpenIndex < 0)
            {
                return new PathComponent(componentText);
            }
            else
            {
                var bracketCloseIndex = componentText.IndexOf(']');

                if (bracketCloseIndex < 0)
                {
                    throw new DeltaException(
                        $"No corresponding closing bracket in property path '{componentText}'");
                }
                if (bracketCloseIndex < bracketOpenIndex)
                {
                    throw new DeltaException(
                        $"Closing bracket before opening bracket in "
                        + $"property path '{componentText}'");
                }
                if (bracketOpenIndex == 0)
                {
                    throw new DeltaException(
                        $"Opening bracket should follow property name in "
                        + $"property path '{componentText}'");
                }

                var inBrackets = componentText.Substring(
                    bracketOpenIndex + 1,
                    Math.Max(0, bracketCloseIndex - bracketOpenIndex - 1));
                int index;

                if (!int.TryParse(inBrackets, out index))
                {
                    throw new DeltaException(
                        $"Brackets should contain an integer in "
                        + $"property path '{componentText}'");
                }

                return new PathComponent(componentText.Substring(0, bracketOpenIndex), index);
            }
        }
    }
}