using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Subdivx.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Subdivx;

public class SubdivxPlugin: BasePlugin<PluginConfiguration>, IHasWebPages
{
    public SubdivxPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }
    public static SubdivxPlugin? Instance { get; private set; }
    public override string Name => "Subdivx";
    public override string Description => "Subtitle provider for subdivx.com";
    public override Guid Id => Guid.Parse("9f420e7a-3ae6-4073-9bc9-81da6dea8143");
    
    public virtual PluginConfiguration GetConfiguration()
    {
        return this.Configuration;
    }
    
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "subdivx",
                EmbeddedResourcePath = GetType().Namespace + ".Web.subdivx.html",
            },
            new PluginPageInfo
            {
                Name = "subdivxjs",
                EmbeddedResourcePath = GetType().Namespace + ".Web.subdivx.js"
            }
        };
    }
}
