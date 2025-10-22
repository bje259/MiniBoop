using System;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Jint;
using Newtonsoft.Json.Linq;

public class BoopScript
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string Author { get; set; }
    public string[] Tags { get; set; }
    public string Icon { get; set; }
    public string JsCode { get; set; }
    public class StateType
    {
        public string fullText { get; set; }
        public string selection { get; set; }
    }

    public string ApplyOld(string inputText, Action<string> postError)
    {
        var engine = new Engine(cfg => cfg.AllowClr());

        var state = new
        {
            text = inputText,
            selection = inputText, // Placeholder, you can later support actual selection logic
            postError = postError
        };

        engine.SetValue("state", JObject.FromObject(state));
        engine.Execute(JsCode);

        // Now check if main exists then call it
        var mainFn = engine.GetValue("main");
        if (!mainFn.IsUndefined())
        {
            engine.Invoke("main", engine.GetValue("state"));
        }
        else
        {
            throw new Exception("Script does not define a main(state) function");
        }
        // After execution, get back state.Text
        var newText = engine.GetValue("state").AsObject().Get("text").AsString();
        return newText;
    }

    public StateType Apply(string inputText, string selectedText, Action<string> postError)
    {
        var engine = new Engine(cfg => cfg.AllowClr());

        // Add postError as a callable JS function
        engine.SetValue("postError", new Action<string>(postError));

        // Create state object directly in JS with postError attached
        engine.Execute(@"
            var state = {
                fullText: " + JsonConvert.SerializeObject(inputText) + @",
                selection: " + JsonConvert.SerializeObject(selectedText) + @",
                postError: postError
            };
            if (state.selection === '') {
                Object.defineProperty(state, 'text', {
					get() { return this.fullText; },
					set(value) { this.fullText = value; },
					enumerable: true,
					configurable: true
				});
            } else {
                Object.defineProperty(state, 'text', {
                    get() { return this.selection; },
                    set(value) { this.selection = value; },
                    enumerable: true,
                    configurable: true
                });
            }
        ");

        // Now check if main exists then call it
        engine.Execute(JsCode);
        var mainFn = engine.GetValue("main");

        if (!mainFn.IsUndefined())
        {
            engine.Invoke("main", engine.GetValue("state"));
        }
        else
        {
            throw new Exception("Script does not define a main(state) function");
        }

        // After execution, get back state.text
        var stateObj = engine.GetValue("state").AsObject();
        var newFullText = stateObj.Get("fullText").AsString();
        var newSelection = stateObj.Get("selection").AsString();
        // if (selectedText != "")
        //     return newFullText.Replace(selectedText, newText);
        // else
        //     return newFullText;
        var newState = new StateType
        {
            fullText = newFullText,
            selection = newSelection
        };
        return newState;
    }

    public static BoopScript LoadFromFile(string path)
    {
        var raw = File.ReadAllText(path);

        // Extract metadata
        var metaMatch = Regex.Match(raw, @"/\*\*([\s\S]*?)\*/", RegexOptions.Multiline);
        // var tmp = Regex.Replace
        if (!metaMatch.Success)
            throw new Exception("Boop metadata block not found.");

        var metaJson = metaMatch.Groups[1].Value
            .Split('\n')
            .Select(l => l.TrimStart('*').Trim())
            .Aggregate((aa, b) => aa + "\n" + b);

        var meta = JsonConvert.DeserializeObject<Dictionary<string, object>>(metaJson);

        return new BoopScript
        {
            Name = meta.TryGetValue("name", out var n) ? n.ToString() : "Unnamed",
            Description = meta.TryGetValue("description", out var d) ? d.ToString() : "",
            Author = meta.TryGetValue("author", out var a) ? a.ToString() : "",
            Tags = meta.TryGetValue("tags", out var t) ? t.ToString().Split(',') : new string[0],
            Icon = meta.TryGetValue("icon", out var i) ? i.ToString() : "",
            JsCode = raw
        };
    }
}