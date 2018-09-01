﻿// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/. 
namespace ServiceStack.Discovery.Redis
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using ServiceStack.DataAnnotations;
    using ServiceStack;

    public static class TypeExtensions
    {
        /// <summary>
        /// Filters out types with <see cref="ExcludeAttribute"/> 
        /// containing values of <see cref="Feature.Metadata"/> or <see cref="Feature.ServiceDiscovery"/>
        /// </summary>
        /// <param name="types">the types to filter</param>
        /// <returns>the filtered types</returns>
        public static IEnumerable<Type> WithServiceDiscoveryAllowed(this IEnumerable<Type> types)
        {
            return types
                .Where(x => x.AllAttributes<ExcludeAttribute>().All(a => (!a.Feature.HasFlag(Feature.Metadata))))
                .Where(x => x.AllAttributes<ExcludeAttribute>().All(a => (!a.Feature.HasFlag(Feature.ServiceDiscovery))));
        }

        /// <summary>
        /// Filters out ServiceStack native type namespaces and types
        /// </summary>
        /// <param name="types">the types to filter</param>
        /// <param name="nativeTypes">the NativeTypesFeature</param>
        /// <returns>the filtered types</returns>
        public static IEnumerable<Type> WithoutNativeTypes(this IEnumerable<Type> types, NativeTypesFeature nativeTypes)
        {
            return nativeTypes == null
                ? types
                : types
                    .Where(x => !nativeTypes.MetadataTypesConfig.IgnoreTypes.Contains(x))
                    .Where(x => !nativeTypes.MetadataTypesConfig.IgnoreTypesInNamespaces.Contains(x.Namespace));
        }
    }
}