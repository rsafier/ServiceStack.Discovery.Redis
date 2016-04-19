/* Options:
Date: 2016-04-18 21:51:28
Version: 4.055
Tip: To override a DTO option, remove "//" prefix before updating
BaseUrl: http://BEING:7777/

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
using TestService1;


namespace TestService1
{
    
    public partial class Service1CallsService2
        : IReturn<string>
    {
        public virtual string From { get; set; }
    }

    [Restrict(AccessTo = RequestAttributes.Jsv)]
    public partial class Service1External
        : IReturn<string>
    {
        public virtual string From { get; set; }
    }
}

