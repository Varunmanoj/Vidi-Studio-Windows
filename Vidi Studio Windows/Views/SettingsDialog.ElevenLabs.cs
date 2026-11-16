using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows.Views;

public sealed partial class SettingsDialog
{
    private readonly StackPanel elevenControls=new(){Spacing=12},elevenFields=new(){Spacing=12},elevenPlan=new(){Spacing=8},elevenRows=new(){Spacing=10};
    private Border? elevenCard;
    private readonly ScrollViewer elevenRowsHost=new(){MaxHeight=360,MinHeight=120,HorizontalScrollMode=ScrollMode.Disabled};
    private bool elevenBuilt,elevenChecking,elevenLoading,elevenHasMore;
#if DEBUG
    private bool elevenFixture;
#endif
    private string elevenKey="",elevenSource="account",elevenSearch="",elevenSort="name",elevenCursor="",elevenModelSort="name";
    private int elevenPage,elevenRevision,elevenQuery,elevenAudioRevision;
    private JsonArray elevenModels=new(),elevenVoices=new(),elevenFormats=new();
    private readonly Dictionary<string,string> elevenFilters=new();
    private JsonObject elevenFilterOptions=new();
    private JsonArray elevenAccents=new();
    private readonly StackPanel elevenMetadataFilters=new(){Spacing=12};
    private readonly TextBlock elevenStatus=new(){Text="No API key"},elevenMessage=new(){TextWrapping=TextWrapping.Wrap};
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? elevenTimer,elevenQueryTimer;
    private Windows.Media.Playback.MediaPlayer? elevenPlayer;
    private string? elevenPlaying;
    private readonly object elevenSampleOwner=new();
    private JsonObject ElevenProfile=>ElevenLabsSpeech.Profile(draft);
    private static string ES(JsonNode? value,string fallback="")=>ElevenLabsSpeech.S(value,fallback);
    private JsonObject? ElevenModel=>elevenModels.OfType<JsonObject>().FirstOrDefault(m=>ES(m["id"])==ES(ElevenProfile["model"]));
    private void PrepareElevenLabs(StackPanel parent)
    {
        elevenCard??=SpeechSettingsCard(elevenControls);
        if(elevenCard.Parent is Panel old)old.Children.Remove(elevenCard);
        parent.Children.Add(elevenCard);
        if(elevenBuilt)return;
        elevenBuilt=true;elevenRowsHost.Content=elevenRows;elevenKey=model.Settings.Get("elevenlabsApiKey");
        elevenTimer=DispatcherQueue.CreateTimer();elevenTimer.Interval=TimeSpan.FromMilliseconds(700);elevenTimer.Tick+=async(_,_)=>{elevenTimer.Stop();await RefreshElevenLabs();};
        elevenQueryTimer=DispatcherQueue.CreateTimer();elevenQueryTimer.Interval=TimeSpan.FromMilliseconds(350);elevenQueryTimer.Tick+=async(_,_)=>{elevenQueryTimer.Stop();await LoadElevenVoices();};
        System.ComponentModel.PropertyChangedEventHandler changed=(_,args)=>{if(args.PropertyName=="Busy"&&!model.Busy&&!closed&&ES(draft["ttsMode"])=="elevenlabs")_ = RefreshElevenLabs();};
        model.PropertyChanged+=changed;
        Closed+=(_,_)=>{model.PropertyChanged-=changed;elevenTimer.Stop();elevenQueryTimer.Stop();StopElevenDemo();elevenPlayer?.Dispose();};
        elevenControls.Children.Add(ScreenReaderHeading.Mark(new TextBlock{Text="ElevenLabs Voices",FontSize=18}));
        var password=new PasswordBox{Password=elevenKey};AutomationProperties.SetAutomationId(password,"elevenlabs-api-key");AutomationProperties.SetName(password,"ElevenLabs API key");
        var passwordMenu=MenuAccessKeys.Create();var edit=new MenuFlyoutItem{Text="Edit ElevenLabs API Key"};AutomationProperties.SetAutomationId(edit,"elevenlabs-key-edit");edit.Click+=(_,_)=>password.Focus(FocusState.Programmatic);passwordMenu.Items.Add(edit);password.ContextFlyout=passwordMenu;
        var row=new Grid{ColumnSpacing=8};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var icon=ApiKeyStatusIcon(elevenStatus,"elevenlabs-key-status");
        var deleteHost=new StackPanel();
        var delete=SpeechAction(deleteHost,"Delete ElevenLabs API Key…","elevenlabs-key-delete",async()=>{
            var confirm=new ContentDialog{Title="Delete ElevenLabs API Key?",PrimaryButtonText="Delete API Key",CloseButtonText="Cancel"};
            if(await WindowedDialog.ShowAsync(confirm,this,460,250)==ContentDialogResult.Primary)password.Password="";
        },elevenKey.Length>0);
        deleteHost.Children.Remove(delete);delete.Content=new FontIcon{Glyph="\uE74D",FontSize=14};delete.MinWidth=36;delete.Padding=new Thickness(8,6,8,6);IconButtonContent.SetDestructive(delete,true);
        AutomationProperties.SetAutomationId(delete,"elevenlabs-key-delete-icon");AutomationProperties.SetName(delete,"Delete ElevenLabs API key");ToolTipService.SetToolTip(delete,"Delete ElevenLabs API key");
        row.Children.Add(password);Grid.SetColumn(icon,1);row.Children.Add(icon);Grid.SetColumn(delete,2);row.Children.Add(delete);AddSettingsRow(elevenControls,"ElevenLabs API key",row,true);
        var buttons=new StackPanel();var check=SpeechAction(buttons,"Check API Key","elevenlabs-key-check",async()=>{elevenTimer.Stop();await RefreshElevenLabs();},elevenKey.Length>0);
        var dashboard=SpeechAction(buttons,"Open ElevenLabs API Keys","elevenlabs-key-dashboard",async()=>await Windows.System.Launcher.LaunchUriAsync(new Uri("https://elevenlabs.io/app/settings/api-keys")));
        SpeechActionsRow(buttons,"elevenlabs-key-actions",check,dashboard);elevenControls.Children.Add(buttons);
        password.PasswordChanged+=(_,_)=>{
            #if DEBUG
            if(elevenFixture)return;
#endif
            var next=password.Password.Trim();if(next==elevenKey)return;
            if(!CredentialStore.ElevenLabs.Write(next)){elevenMessage.Text="Could not save the ElevenLabs API key to Credential Manager.";return;}
            var previous=elevenKey;elevenKey=next;elevenRevision++;elevenQuery++;StopElevenDemo();elevenTimer.Stop();
            elevenModels=new();elevenVoices=new();elevenFilterOptions=new();elevenAccents=new();elevenPlan.Children.Clear();elevenRows.Children.Clear();elevenFields.Children.Clear();elevenChecking=false;elevenLoading=false;draft["elevenlabsKeyStatus"]="unverified";draft["elevenlabsCredentialRevision"]=SettingsStore.GetDouble(draft["elevenlabsCredentialRevision"],0)+1;
            elevenStatus.Text=next.Length==0?"No API key":"API key saved — not yet checked";check.IsEnabled=delete.IsEnabled=next.Length>0;
            if(previous.Length>0)_=ForgetElevenKey(previous);
            if(next.Length>0)elevenTimer.Start();
        };
        elevenControls.Children.Add(elevenPlan);elevenControls.Children.Add(elevenFields);elevenControls.Children.Add(elevenMessage);
        elevenStatus.Text=elevenKey.Length==0?"No API key":ES(draft["elevenlabsKeyStatus"])=="verified"?"API key verified":"API key saved — not yet checked";
    }
    private async Task ForgetElevenKey(string key){try{await ElevenLabsSpeech.Request("forget",key);}catch(Exception){}}
    private async Task RefreshElevenLabs()
    {
        if(closed||elevenChecking||elevenKey.Length==0)return;
        var revision=elevenRevision;var key=elevenKey;elevenChecking=true;elevenStatus.Text="Checking API key…";
        try {
            var result=await ElevenLabsSpeech.Request("check",key,cancellation:lifetime.Token);if(closed||revision!=elevenRevision)return;
            draft["elevenlabsKeyStatus"]=ES(result["keyStatus"],"unavailable");elevenStatus.Text=ES(result["error"],ES(result["message"],"Key check unavailable"));
            RenderElevenPlan(result);
            if(ES(result["kind"])=="invalid")return;
            var catalog=await ElevenLabsSpeech.Request("models",key,cancellation:lifetime.Token);if(closed||revision!=elevenRevision)return;
            elevenModels=(catalog["models"]?.DeepClone() as JsonArray)??new();elevenFormats=(catalog["formats"]?.DeepClone() as JsonArray)??new();elevenAccents=(catalog["filterOptions"]?["accent"]?.DeepClone() as JsonArray)??new();
            elevenMessage.Text=ES(catalog["error"],SettingsStore.GetBool(catalog["modelsCached"])?"Using saved models":"");
            if(ES(ElevenProfile["model"]).Length==0&&elevenModels.FirstOrDefault() is JsonObject first)SelectElevenModel(ES(first["id"]));
            BuildElevenFields();await LoadElevenVoices();
        }catch(OperationCanceledException){}catch(Exception){if(!closed&&revision==elevenRevision)elevenStatus.Text="Could not check the key. Check your connection.";}
        finally{if(revision==elevenRevision)elevenChecking=false;}
    }
    private void RenderElevenPlan(JsonObject result)
    {
        elevenPlan.Children.Clear();elevenPlan.Children.Add(ScreenReaderHeading.Mark(new TextBlock{Text="Account Plan",FontSize=16}));
        if(result["plan"] is not JsonObject plan){elevenPlan.Children.Add(new TextBlock{Text="Plan details unavailable"});return;}
        void Row(string title,string text){if(text.Length>0)AddSettingsRow(elevenPlan,title,new TextBlock{Text=text,VerticalAlignment=VerticalAlignment.Center});}
        string Friendly(string key)=>System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(ES(plan[key]).Replace('_',' '));
        Row("Plan",Friendly("tier"));Row("Status",Friendly("status"));
        if(plan["character_count"]!=null&&plan["character_limit"]!=null){var used=SettingsStore.GetDouble(plan["character_count"],0);var limit=SettingsStore.GetDouble(plan["character_limit"],0);Row("Usage this period",used.ToString("N0")+" / "+limit.ToString("N0"));Row("Remaining allowance",Math.Max(0,limit-used).ToString("N0"));}
        if(plan["next_character_count_reset_unix"]!=null)Row("Allowance resets",DateTimeOffset.FromUnixTimeSeconds((long)SettingsStore.GetDouble(plan["next_character_count_reset_unix"],0)).ToLocalTime().ToString("d"));
        if(plan["voice_slots_used"]!=null&&plan["voice_limit"]!=null)Row("Saved voices",ES(plan["voice_slots_used"])+" / "+ES(plan["voice_limit"]));
        Row("Billing period",ES(plan["billing_period"]) switch{"monthly_period"=>"Monthly","annual_period"=>"Annual","3_month_period"=>"Every 3 months","6_month_period"=>"Every 6 months",_=>Friendly("billing_period")});
        if(ES(result["planMessage"]).Length>0)elevenPlan.Children.Add(new TextBlock{Text=ES(result["planMessage"])});
    }
    private void SelectElevenModel(string id)
    {
        var profile=ElevenProfile;profile["model"]=id;
        profile["controls"]=new JsonArray((ElevenModel?["controls"] as JsonArray??new()).Where(n=>ES(n)!="cloneStrength").Select(n=>n?.DeepClone()).ToArray());
        if(ES(profile["language"],"auto")!="auto"&&!(ElevenModel?["languages"] as JsonArray??new()).Any(n=>ES(n?["id"])==ES(profile["language"])))profile["language"]="auto";
        StopElevenDemo();
    }
    private void SelectElevenVoice(JsonObject voice)
    {
        ElevenProfile["voice"]=ES(voice["id"]);ElevenProfile["voiceName"]=ES(voice["name"]);ElevenProfile["originalVoice"]=ES(voice["original"]);
        ElevenProfile["controls"]=new JsonArray((ElevenModel?["controls"] as JsonArray??new()).Where(n=>ES(n)!="cloneStrength"||(ES(voice["category"])=="professional"&&ES(voice["tuning"]?[ES(ElevenProfile["model"])])=="fine_tuned")).Select(n=>n?.DeepClone()).ToArray());
        StopElevenDemo();BuildElevenFields();
    }
    private void ElevenSearchField(StackPanel parent,string title,string id,string text,Action<string> change)
    {
        var field=new TextBox{Text=text,PlaceholderText=title};AutomationProperties.SetName(field,title);AutomationProperties.SetAutomationId(field,id);
        field.TextChanged+=(_,_)=>change(field.Text);
        var menu=MenuAccessKeys.Create();var focusItem=new MenuFlyoutItem{Text="Focus "+title};AutomationProperties.SetAutomationId(focusItem,id+"-focus");focusItem.Click+=(_,_)=>field.Focus(FocusState.Programmatic);menu.Items.Add(focusItem);
        var clear=new MenuFlyoutItem{Text="Clear "+title};AutomationProperties.SetAutomationId(clear,id+"-clear");clear.Click+=(_,_)=>field.Text="";menu.Items.Add(clear);field.ContextFlyout=menu;AddSettingsRow(parent,title,field,true);
    }
    private void ScheduleElevenQuery(){elevenQueryTimer?.Stop();elevenQueryTimer?.Start();}
    private void BuildElevenFields()
    {
        elevenFields.Children.Clear();var profile=ElevenProfile;
        elevenFields.Children.Add(ScreenReaderHeading.Mark(new TextBlock{Text="Models and Voices",FontSize=16}));
        SpeechChoiceControl(elevenFields,"Sort models","elevenlabs-model-sort",elevenModelSort,new[]{("name","Model name (A–Z)"),("descending","Model name (Z–A)")},value=>{elevenModelSort=value;BuildElevenFields();});
        var models=elevenModels.OfType<JsonObject>();
        models=elevenModelSort=="descending"?models.OrderByDescending(m=>ES(m["name"]),StringComparer.CurrentCultureIgnoreCase):models.OrderBy(m=>ES(m["name"]),StringComparer.CurrentCultureIgnoreCase);
        SpeechChoiceControl(elevenFields,"Model","elevenlabs-model",ES(profile["model"]),models.Select(m=>(ES(m["id"]),ES(m["name"]))),value=>{SelectElevenModel(value);BuildElevenFields();ScheduleElevenQuery();});
        SpeechAction(elevenFields,"Refresh Models and Voices","elevenlabs-refresh",async()=>await RefreshElevenLabs(),elevenKey.Length>0);
        SpeechChoiceControl(elevenFields,"Narration language","elevenlabs-language",ES(profile["language"],"auto"),new[]{("auto","Automatic")}.Concat((ElevenModel?["languages"] as JsonArray??new()).Select(n=>(ES(n?["id"]),ES(n?["name"])))),value=>{profile["language"]=value;StopElevenDemo();ScheduleElevenQuery();});
        SpeechChoiceControl(elevenFields,"Voice source","elevenlabs-source",elevenSource,new[]{("account","My Voices"),("library","Voice Library")},value=>{elevenSource=value;elevenSort=value=="library"?"trending":"name";elevenFilters.Clear();BuildElevenFields();ScheduleElevenQuery();});
        ElevenSearchField(elevenFields,"Search voices","elevenlabs-voice-search",elevenSearch,value=>{elevenSearch=value;ScheduleElevenQuery();});
        var sorts=elevenSource=="library"?new[]{("trending","Trending"),("created_date","Newest"),("usage_character_count_1y","Most used"),("cloned_by_count","Most saved")}:new[]{("name","Voice name (A–Z)"),("descending","Voice name (Z–A)"),("newest","Newest")};
        SpeechChoiceControl(elevenFields,"Sort voices","elevenlabs-voice-sort",elevenSort,sorts,value=>{elevenSort=value;ScheduleElevenQuery();});
        void Filter(string title,string key,IEnumerable<(string,string)> choices){SpeechChoiceControl(elevenFields,title,key switch {"category"=>"elevenlabs-category","gender"=>"elevenlabs-gender","age"=>"elevenlabs-age",_=>"elevenlabs-"+key},elevenFilters.GetValueOrDefault(key,"all"),choices,value=>{elevenFilters[key]=value;ScheduleElevenQuery();});}
        Filter("Voice type","category",new[]{("all","All types")}.Concat(elevenSource=="library"?new[]{("professional","Professional"),("high_quality","Studio quality")}:new[]{("premade","Default"),("cloned","Cloned"),("generated","Designed"),("professional","Professional")}));
        Filter("Gender","gender",new[]{("all","All genders"),("female","Female"),("male","Male"),("other","Other")});
        Filter("Age","age",new[]{("all","All ages"),("young","Young"),("middle_aged","Middle-aged"),("old","Older")});
        elevenFields.Children.Add(elevenMetadataFilters);RenderElevenMetadataFilters();
        elevenFields.Children.Add(new TextBlock{Text=ES(profile["voice"]).Length==0?"No voice selected":"Selected: "+ES(profile["voiceName"])});
        elevenFields.Children.Add(elevenRowsHost);RenderElevenRows();
        var actions=new StackPanel();var more=SpeechAction(actions,"Load More Voices","elevenlabs-more",async()=>await LoadElevenVoices(true),elevenHasMore&&!elevenLoading);
        var reset=SpeechAction(actions,"Reset Voice Filters","elevenlabs-filters-reset",()=>{elevenSearch="";elevenFilters.Clear();elevenSort=elevenSource=="library"?"trending":"name";BuildElevenFields();ScheduleElevenQuery();});SpeechActionsRow(actions,"elevenlabs-browser-actions",more,reset);elevenFields.Children.Add(actions);
        elevenFields.Children.Add(ScreenReaderHeading.Mark(new TextBlock{Text="Voice Controls",FontSize=16}));
        foreach(var field in (profile["controls"] as JsonArray??new()).Select(n=>ES(n))) {
            if(field=="speakerBoost") {var toggle=new ToggleSwitch{IsOn=SettingsStore.GetBool(profile[field],true)};toggle.Toggled+=(_,_)=>profile[field]=toggle.IsOn;AutomationProperties.SetAutomationId(toggle,"elevenlabs-speaker-boost");var menu=MenuAccessKeys.Create();var item=new ToggleMenuFlyoutItem{Text="Speaker boost"};item.Click+=(_,_)=>toggle.IsOn=!toggle.IsOn;menu.Opening+=(_,_)=>item.IsChecked=toggle.IsOn;menu.Items.Add(item);toggle.ContextFlyout=menu;AddSettingsRow(elevenFields,"Speaker boost",toggle);continue;}
            if(field=="stabilityMode"){SpeechChoiceControl(elevenFields,"Stability","elevenlabs-stability-mode",SettingsStore.GetDouble(profile[field],.5).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture),new[]{("0.0","Creative"),("0.5","Natural"),("1.0","Robust")},value=>profile[field]=double.Parse(value,System.Globalization.CultureInfo.InvariantCulture));continue;}
            var title=field switch{"speed"=>"Speaking speed","cloneStrength"=>"Clone strength",_=>System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(field)};
            SpeechAdjustmentControl(elevenFields,title,field=="cloneStrength"?"elevenlabs-clone-strength":"elevenlabs-"+field,profile,field,field=="speed"?1:field=="similarity"?.75:field=="stability"?.5:0,field=="speed"?.7:0,field=="speed"?1.2:1);
        }
        SpeechChoiceControl(elevenFields,"Audio quality","elevenlabs-format",ES(profile["format"],"mp3_44100_128"),elevenFormats.Select(n=>(ES(n?["id"]),ES(n?["name"]))),value=>profile["format"]=value);
        SpeechAction(elevenFields,"Restore Voice Defaults","elevenlabs-controls-reset",()=>{foreach(var (key,value) in new[]{("stability",.5),("stabilityMode",.5),("similarity",.75),("style",0.0),("speed",1.0),("cloneStrength",0.0)})profile[key]=value;profile["speakerBoost"]=true;profile["format"]="mp3_44100_128";BuildElevenFields();});
    }
    private void RenderElevenMetadataFilters()
    {
        elevenMetadataFilters.Children.Clear();
        foreach(var (title,key,all) in new[]{("Accent","accent","All accents"),("Use case","useCase","All use cases"),("Style","style","All styles")}) {
            if(key=="style"&&elevenSource!="library")continue;
            var options=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var option in (elevenFilterOptions[key] as JsonArray??new()).OfType<JsonObject>())options[ES(option["id"])]=ES(option["name"]);
            if(key=="accent")foreach(var option in elevenAccents.OfType<JsonObject>())options[ES(option["id"])]=ES(option["name"]);
            foreach(var voice in elevenVoices.OfType<JsonObject>()){var value=ES(voice[key]);if(value.Length>0&&!options.ContainsKey(value))options[value]=ElevenFriendly(value);}
            var selected=elevenFilters.GetValueOrDefault(key,"all");if(selected!="all"&&!options.ContainsKey(selected))options[selected]=ElevenFriendly(selected);
            var choices=new[]{("all",all)}.Concat(options.OrderBy(item=>item.Value,StringComparer.CurrentCultureIgnoreCase).Select(item=>(item.Key,item.Value)));
            SpeechChoiceControl(elevenMetadataFilters,title,"elevenlabs-filter-"+key,selected,choices,value=>{elevenFilters[key]=value;ScheduleElevenQuery();});
        }
    }
    private async Task LoadElevenVoices(bool more=false)
    {
        if(closed||elevenKey.Length==0||elevenModels.Count==0)return;
        var query=++elevenQuery;var revision=elevenRevision;elevenLoading=true;
        var request=new JsonObject{["source"]=elevenSource,["search"]=elevenSearch,["sort"]=elevenSort,["language"]=ES(ElevenProfile["language"],"auto")};
        foreach(var item in elevenFilters)request[item.Key]=item.Value;
        if(more){request["cursor"]=elevenCursor;request["page"]=elevenPage+1;}
        try {
            var result=await ElevenLabsSpeech.Request("voices",elevenKey,request,lifetime.Token);if(closed||query!=elevenQuery||revision!=elevenRevision)return;
            if(result["voices"] is JsonArray rows){if(!more)elevenVoices=new();foreach(var voice in rows.OfType<JsonObject>())if(!elevenVoices.Any(v=>ES(v?["id"])==ES(voice["id"])))elevenVoices.Add(voice.DeepClone());}
            elevenFilterOptions=(result["filterOptions"]?.DeepClone() as JsonObject)??new();RenderElevenMetadataFilters();
            elevenHasMore=SettingsStore.GetBool(result["hasMore"]);elevenCursor=ES(result["cursor"]);elevenPage=(int)SettingsStore.GetDouble(result["page"],0);elevenMessage.Text=ES(result["error"],SettingsStore.GetBool(result["voicesCached"])?"Using saved voices":"");
            RenderElevenRows();
            foreach(var button in SpeechItems<Button>(elevenFields).Where(b=>AutomationProperties.GetAutomationId(b)=="elevenlabs-more"))button.IsEnabled=elevenHasMore;
        }catch(OperationCanceledException){}catch(Exception){if(!closed&&query==elevenQuery)elevenMessage.Text="Could not load voices. Try again.";}finally{if(query==elevenQuery)elevenLoading=false;}
    }
    private static string ElevenLanguage(string value)
    {
        if(value.Length==0)return "";
        try{return System.Globalization.CultureInfo.GetCultureInfo(value).DisplayName;}catch(System.Globalization.CultureNotFoundException){return value;}
    }
    private static string ElevenFriendly(string value)=>System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.Replace('_',' '));
    private Button ElevenDemoButton(JsonObject voice,JsonObject? variant=null)
    {
        var id=ES(voice["id"]);var active=elevenPlaying==id;
        var available=variant!=null?ES(variant["preview"]).Length>0:ES(voice["preview"]).Length>0||(voice["variants"] as JsonArray??new()).Any(v=>ES(v?["preview"]).Length>0);
        var host=new StackPanel();
        var button=SpeechAction(host,active?"Stop Demo":"Play Demo",(variant==null?"elevenlabs-demo-":"elevenlabs-demo-variant-")+id+ES(variant?["locale"]),async()=>{if(elevenPlaying==id){StopElevenDemo();RenderElevenRows();}else await PlayElevenDemo(voice,variant);},available&&(active||!VoiceSampleSession.Busy));
        host.Children.Remove(button);
        button.Content=new FontIcon{Glyph=active?"\uE71A":"\uE768",FontSize=14};button.MinWidth=36;button.Padding=new Thickness(8,6,8,6);
        var label=(active?"Stop demo of ":"Play demo of ")+ES(voice["name"])+(variant==null?"":", "+ElevenLanguage(ES(variant["locale"],ES(variant["language"]))));
        AutomationProperties.SetName(button,label);ToolTipService.SetToolTip(button,label);return button;
    }
    private void RenderElevenRows()
    {
        elevenRows.Children.Clear();
        foreach(var voice in elevenVoices.OfType<JsonObject>()) {
            var id=ES(voice["id"]);var family=new StackPanel{Spacing=6};family.Children.Add(ScreenReaderHeading.Mark(new TextBlock{Text=ES(voice["name"]),FontSize=16}));
            var detail=string.Join(" · ",new[]{ElevenLanguage(ES(voice["language"])),ElevenFriendly(ES(voice["accent"])),ElevenFriendly(ES(voice["gender"])),ElevenFriendly(ES(voice["style"])),ElevenFriendly(ES(voice["useCase"]))}.Where(v=>v.Length>0));
            var selected=ES(ElevenProfile["voice"])==id||(ES(ElevenProfile["originalVoice"]).Length>0&&ES(ElevenProfile["originalVoice"])==ES(voice["original"]));
            async Task Choose() {
                if(elevenSource!="library"){SelectElevenVoice(voice);return;}
                var confirm=new ContentDialog{Title="Add Voice to ElevenLabs?",Content=new TextBlock{Text=ES(voice["name"]),TextWrapping=TextWrapping.Wrap},PrimaryButtonText="Add & Select",CloseButtonText="Cancel"};
                if(await WindowedDialog.ShowAsync(confirm,this,480,280)!=ContentDialogResult.Primary){RenderElevenRows();return;}
                var revision=elevenRevision;
                try{var result=await ElevenLabsSpeech.Request("add",elevenKey,new JsonObject{["voice"]=id},lifetime.Token);if(closed||revision!=elevenRevision)return;elevenMessage.Text=ES(result["error"]);if(result["addedVoice"] is JsonObject added){SelectElevenVoice(added);await RefreshElevenLabs();}}catch(Exception){if(!closed)elevenMessage.Text="Could not add this voice. Refresh My Voices before trying again.";}
                if(!closed)RenderElevenRows();
            }
            var select=new RadioButton{Content=new TextBlock{Text=detail,TextWrapping=TextWrapping.Wrap},IsChecked=selected,GroupName="elevenlabs-voices",HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
            AutomationProperties.SetAutomationId(select,"elevenlabs-select-"+id);AutomationProperties.SetName(select,(elevenSource=="library"?"Add & select ":"Select ")+ES(voice["name"])+", "+detail);ToolTipService.SetToolTip(select,(elevenSource=="library"?"Add & select ":"Select ")+ES(voice["name"]));
            select.Checked+=async(_,_)=>await Choose();
            var menu=MenuAccessKeys.Create();var selection=new ToggleMenuFlyoutItem{Text="Select "+ES(voice["name"]),IsChecked=selected};selection.Click+=async(_,_)=>await Choose();menu.Items.Add(selection);select.ContextFlyout=menu;
            var row=new Grid{ColumnSpacing=10};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.Children.Add(select);
            var play=ElevenDemoButton(voice);Grid.SetColumn(play,1);row.Children.Add(play);family.Children.Add(row);
            if(!play.IsEnabled&&ES(voice["preview"]).Length==0&&(voice["variants"] as JsonArray??new()).Count==0)family.Children.Add(new TextBlock{Text="Sample unavailable",Opacity=.7});
            var variants=(voice["variants"] as JsonArray??new()).OfType<JsonObject>().Where(v=>ES(v["model"])==ES(ElevenProfile["model"])).GroupBy(v=>ES(v["language"])+"|"+ES(v["locale"])+"|"+ES(v["accent"])).Select(g=>g.First());
            foreach(var variant in variants) {
                var variantRow=new Grid{ColumnSpacing=10,Margin=new Thickness(24,0,0,0)};variantRow.ColumnDefinitions.Add(new ColumnDefinition());variantRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
                variantRow.Children.Add(new TextBlock{Text=string.Join(" · ",new[]{ElevenLanguage(ES(variant["locale"],ES(variant["language"]))),ElevenFriendly(ES(variant["accent"]))}.Where(v=>v.Length>0)),Opacity=.7,TextWrapping=TextWrapping.Wrap});
                var demo=ElevenDemoButton(voice,variant);Grid.SetColumn(demo,1);variantRow.Children.Add(demo);family.Children.Add(variantRow);
            }
            elevenRows.Children.Add(family);
        }
        if(elevenVoices.Count==0)elevenRows.Children.Add(new TextBlock{Text=elevenLoading?"Loading voices…":"No matching voices"});
    }
    private void StopElevenDemo(){elevenAudioRevision++;elevenPlayer?.Pause();elevenPlayer?.Dispose();elevenPlayer=null;elevenPlaying=null;VoiceSampleSession.End(elevenSampleOwner);}
    private async Task PlayElevenDemo(JsonObject voice,JsonObject? variant=null)
    {
        if(!VoiceSampleSession.Begin(elevenSampleOwner))return;
        var request=++elevenAudioRevision;elevenPlaying=ES(voice["id"]);RenderElevenRows();
        try {
            var result=await ElevenLabsSpeech.Request("demo",elevenKey,new JsonObject{["voice"]=ES(voice["id"]),["model"]=ES(ElevenProfile["model"]),["language"]=variant==null?ES(ElevenProfile["language"]):ES(variant["language"]),["locale"]=ES(variant?["locale"])},lifetime.Token);
            if(closed||request!=elevenAudioRevision)return;
            if(ES(result["demoPath"]).Length==0){elevenMessage.Text=ES(result["error"],"Sample unavailable");StopElevenDemo();RenderElevenRows();return;}
            elevenPlayer=new Windows.Media.Playback.MediaPlayer{Source=Windows.Media.Core.MediaSource.CreateFromUri(new Uri(ES(result["demoPath"])))};
            elevenPlayer.MediaEnded+=(_,_)=>DispatcherQueue.TryEnqueue(()=>{StopElevenDemo();if(!closed)RenderElevenRows();});
            elevenPlayer.MediaFailed+=(_,_)=>DispatcherQueue.TryEnqueue(()=>{StopElevenDemo();if(!closed){elevenMessage.Text="Could not play this demo.";RenderElevenRows();}});elevenPlayer.Play();
        }catch(Exception){if(request==elevenAudioRevision&&!closed){StopElevenDemo();elevenMessage.Text="Could not play this demo.";RenderElevenRows();}}
    }
#if DEBUG
    internal async Task VerifyElevenLabsForSmokeTestAsync(Func<string,Task> capture)
    {
        var before=draft.DeepClone().AsObject();var originalKey=elevenKey;var oldModels=elevenModels;var oldVoices=elevenVoices;
        elevenFixture=true;elevenTimer?.Stop();elevenQueryTimer?.Stop();elevenKey="";
        try {
            elevenModels=JsonNode.Parse("""[{"id":"future-model","name":"Future Model","controls":["stability","similarity","speed","speakerBoost"],"languages":[{"id":"en","name":"English"}]}]""")!.AsArray();
            elevenVoices=JsonNode.Parse("""[{"id":"voice-a","name":"Sample voice","language":"en","accent":"British","preview":"https://storage.googleapis.com/example.mp3","variants":[]},{"id":"voice-b","name":"Sample voice","language":"hi","accent":"Indian","preview":"","variants":[]}]""")!.AsArray();
            elevenFormats=JsonNode.Parse("""[{"id":"mp3_44100_128","name":"MP3 · 44.1 kHz · 128 kbps"}]""")!.AsArray();
            draft["ttsMode"]="elevenlabs";SelectElevenModel("future-model");SelectElevenVoice(elevenVoices[0]!.AsObject());
            NavigateForSmokeTest(0);NavigateForSmokeTest(2);mode.SelectedItem=((IEnumerable<SpeechEngineOption>)mode.ItemsSource).First(option=>option.Id=="elevenlabs");await Task.Delay(150);BuildElevenFields();
            var password=SpeechItems<PasswordBox>(elevenControls).Single();var stored=password.Password;password.Password="isolated-fixture-key";elevenStatus.Text="API key verified";
            RenderElevenPlan(JsonNode.Parse("""{"plan":{"tier":"future_tier","status":"active","character_count":1200,"character_limit":1000,"voice_slots_used":2,"voice_limit":10,"billing_period":"monthly_period"}}""")!.AsObject());
            foreach(var theme in new[]{"dark","light"}) {
                ApplyWindowTheme(theme);((FrameworkElement)password.Parent).StartBringIntoView(new BringIntoViewOptions{VerticalAlignmentRatio=0,AnimationDesired=false});await Task.Delay(150);layout.UpdateLayout();
                var status=SpeechItems<FontIcon>(elevenControls).Single(i=>AutomationProperties.GetAutomationId(i)=="elevenlabs-key-status");
                var trash=SpeechItems<Button>(elevenControls).Single(i=>AutomationProperties.GetAutomationId(i)=="elevenlabs-key-delete-icon");
                var keyPoint=password.TransformToVisual(elevenControls).TransformPoint(new Windows.Foundation.Point());
                var statusPoint=status.TransformToVisual(elevenControls).TransformPoint(new Windows.Foundation.Point());
                var trashPoint=trash.TransformToVisual(elevenControls).TransformPoint(new Windows.Foundation.Point());
                if(statusPoint.X<=keyPoint.X||trashPoint.X<=statusPoint.X||Math.Abs(statusPoint.Y+status.ActualHeight/2-keyPoint.Y-password.ActualHeight/2)>2)throw new InvalidOperationException($"ElevenLabs status/trash must be inline with its key: key={keyPoint.X},{keyPoint.Y},{password.ActualWidth},{password.ActualHeight}; status={statusPoint.X},{statusPoint.Y},{status.ActualWidth},{status.ActualHeight}; trash={trashPoint.X},{trashPoint.Y}; visible={elevenCard?.Visibility}");
                if(SpeechItems<TextBlock>(elevenPlan).Count(t=>t.Text=="Remaining allowance")!=1||!SpeechItems<TextBlock>(elevenPlan).Any(t=>t.Text=="0"))throw new InvalidOperationException("ElevenLabs plan allowance rendering failed.");
                var speed=SpeechItems<Slider>(elevenFields).Single(slider=>AutomationProperties.GetAutomationId(slider)=="elevenlabs-speed");
                if(Math.Abs(speed.Minimum-.7)>.001||Math.Abs(speed.Maximum-1.2)>.001||Math.Abs(speed.Value-1)>.001||AutomationProperties.GetName(speed)!="Speaking speed")throw new InvalidOperationException("ElevenLabs speaking-speed range/default/name regressed.");
                speed.Value=1.1;if(Math.Abs(SettingsStore.GetDouble(ElevenProfile["speed"],0)-1.1)>.001)throw new InvalidOperationException("ElevenLabs speaking speed did not save.");speed.Value=1;
                var rows=SpeechItems<Grid>(elevenControls).Where(g=>g.Tag as string=="settings-row");
                foreach(var row in rows.Where(r=>r.ActualWidth>=480))if(row.Children.Count>=2&&row.Children[1] is FrameworkElement field&&Grid.GetColumn(field)!=1)throw new InvalidOperationException("ElevenLabs fields lost inline alignment.");
                var actions=SpeechItems<Panel>(elevenControls).Single(p=>AutomationProperties.GetAutomationId(p)=="elevenlabs-key-actions");
                var buttons=actions.Children.OfType<Button>().ToArray();if(actions.ActualWidth>buttons.Sum(b=>b.DesiredSize.Width)+8&&Math.Abs(buttons[0].TransformToVisual(actions).TransformPoint(new Windows.Foundation.Point()).Y-buttons[1].TransformToVisual(actions).TransformPoint(new Windows.Foundation.Point()).Y)>1)throw new InvalidOperationException("ElevenLabs key actions stacked when they fit.");
                if(SpeechItems<TextBox>(elevenFields).Count()!=1||SpeechItems<ComboBox>(elevenMetadataFilters).Count()!=2)throw new InvalidOperationException("Only voice search may be a text box; metadata filters must be dropdowns.");
                if(SpeechItems<RadioButton>(elevenRows).Count(b=>AutomationProperties.GetAutomationId(b).StartsWith("elevenlabs-select-"))!=2)throw new InvalidOperationException("Distinct voices with the same name were merged.");
                var selections=SpeechItems<RadioButton>(elevenRows).ToArray();var demos=SpeechItems<Button>(elevenRows).Where(b=>AutomationProperties.GetAutomationId(b).StartsWith("elevenlabs-demo-")).ToArray();
                if(selections.Any(b=>b.Content is not TextBlock)||demos.Length!=2||demos.Any(b=>b.Content is not FontIcon||!AutomationProperties.GetName(b).Contains("demo of")))throw new InvalidOperationException("ElevenLabs voice selection/demo icons or accessible names regressed.");
                if(!selections[0].IsChecked.GetValueOrDefault()||demos[1].IsEnabled)throw new InvalidOperationException("ElevenLabs selected voice and unavailable demo state regressed.");
                foreach(var button in demos){var parent=(Grid)button.Parent;var select=parent.Children.OfType<RadioButton>().Single();if(Math.Abs(button.TransformToVisual(parent).TransformPoint(new Windows.Foundation.Point()).Y+button.ActualHeight/2-select.TransformToVisual(parent).TransformPoint(new Windows.Foundation.Point()).Y-select.ActualHeight/2)>2)throw new InvalidOperationException("ElevenLabs demo icon lost row alignment.");}
                await capture(theme);
                selections[0].StartBringIntoView(new BringIntoViewOptions{VerticalAlignmentRatio=0,AnimationDesired=false});await Task.Delay(150);await capture(theme+"-voices");
            }
            password.Password=stored;
            if(SettingsStore.NormalizeNarrationPlayback(draft)&&ES(draft["narrationPlayback"])!="generated")throw new InvalidOperationException("ElevenLabs cloud playback was not normalized.");
        }
        finally {
            StopElevenDemo();elevenModels=oldModels;elevenVoices=oldVoices;elevenKey=originalKey;elevenFixture=false;
            draft.Clear();foreach(var value in before)draft[value.Key]=value.Value?.DeepClone();BuildElevenFields();NavigateForSmokeTest(0);NavigateForSmokeTest(2);ApplyPreferences();
        }
    }
#endif

}
