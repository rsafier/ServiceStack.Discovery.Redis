/* Options:
Date: 2016-04-18 21:52:29
Version: 4.055
Tip: To override a DTO option, remove "//" prefix before updating
BaseUrl: http://BEING:9999/

//GlobalNamespace: 
//MakePartial: True
//MakeVirtual: True
//MakeDataContractsExtensible: False
//AddReturnMarker: True
//AddDescriptionAsComments: True
//AddDataContractAttributes: False
//AddIndexesToDataMembers: False
//AddGeneratedCodeAttributes: False
//AddResponseStatus: False
//AddImplicitVersion: 
//InitializeCollections: True
//IncludeTypes: 
//ExcludeTypes: 
//AddDefaultXmlNamespace: http://schemas.servicestack.net/types
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using ServiceStack;
using ServiceStack.DataAnnotations;
using TestService2;


namespace TestService2
{

    public partial class ExcludedService2
        : IReturnVoid
    {
    }

    public partial class Service2CallsService1
        : IReturn<string>
    {
        public virtual string From { get; set; }
    }

    public partial class Service2External
        : IReturn<string>
    {
        public virtual string From { get; set; }
    }
}

