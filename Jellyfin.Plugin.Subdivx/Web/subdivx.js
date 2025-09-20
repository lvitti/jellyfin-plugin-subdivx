const SubdivxConfig = {
    pluginUniqueId: '9f420e7a-3ae6-4073-9bc9-81da6dea8143'
};

export default function (view, params) {
    view.addEventListener('viewshow', function () {
        Dashboard.showLoadingMsg();
        const page = this;

        ApiClient.getPluginConfiguration(SubdivxConfig.pluginUniqueId).then(function (config) {
            page.querySelector('#token').value = config.Token || '';
            // Load new options
            const useOriginalTitle = page.querySelector('#useOriginalTitle');
            const showTitleInResult = page.querySelector('#showTitleInResult');
            const showUploaderInResult = page.querySelector('#showUploaderInResult');
            const showDescriptionInResult = page.querySelector('#showDescriptionInResult');
            const subXApiUrl = page.querySelector('#subXApiUrl');

            if (useOriginalTitle) useOriginalTitle.checked = !!config.UseOriginalTitle;
            if (showTitleInResult) showTitleInResult.checked = !!config.ShowTitleInResult;
            if (showUploaderInResult) showUploaderInResult.checked = !!config.ShowUploaderInResult;
            if (showDescriptionInResult) showDescriptionInResult.checked = !!config.ShowDescriptionInResult;
            if (subXApiUrl) subXApiUrl.value = config.SubXApiUrl || '';
            Dashboard.hideLoadingMsg();
        }).catch(function () {
            Dashboard.hideLoadingMsg();
            Dashboard.processErrorResponse({ statusText: "Failed to load plugin configuration" });
        });
    });

    view.querySelector('#SubdivxConfigForm').addEventListener('submit', function (e) {
        e.preventDefault();
        Dashboard.showLoadingMsg();
        const form = this;
        ApiClient.getPluginConfiguration(SubdivxConfig.pluginUniqueId).then(function (config) {
            // Save fields
            config.Token = form.querySelector('#token').value.trim();
            const useOriginalTitle = form.querySelector('#useOriginalTitle');
            const showTitleInResult = form.querySelector('#showTitleInResult');
            const showUploaderInResult = form.querySelector('#showUploaderInResult');
            const subXApiUrl = form.querySelector('#subXApiUrl');

            if (useOriginalTitle) config.UseOriginalTitle = !!useOriginalTitle.checked;
            if (showTitleInResult) config.ShowTitleInResult = !!showTitleInResult.checked;
            if (showUploaderInResult) config.ShowUploaderInResult = !!showUploaderInResult.checked;
            if (subXApiUrl) {
                const apiUrl = (subXApiUrl.value || '').trim();
                if (apiUrl) {
                    config.SubXApiUrl = apiUrl;
                }
            }
            ApiClient.updatePluginConfiguration(SubdivxConfig.pluginUniqueId, config).then(function (result) {
                Dashboard.processPluginConfigurationUpdateResult(result);
            });

        }).catch(function () {
            Dashboard.hideLoadingMsg();
            Dashboard.processErrorResponse({ statusText: "Failed to load plugin configuration" });
        });
        return false;
    });
}
