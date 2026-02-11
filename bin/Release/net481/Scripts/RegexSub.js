/**
    {
        "api":1,
        "name":"Regex Substitution",
        "description":"Search for a regex pattern in the text, then replace it.",
        "author":"bje",
        "icon":"elephant",
        "tags":"regex,search,substitution"
    }
**/

// function main(state) {
//   try {
//     const lines = state.text.split(/\r?\n/);

//     // Matches: s<delimiter><pattern><delimiter><replacement><delimiter><flags>
//     const rule = /^s(?<d>[^A-Za-z0-9\s])(?<pat>(?:\\.|(?!\k<d>).)+)\k<d>(?<rep>(?:\\.|(?!\k<d>).)*)\k<d>(?<flags>[a-z]*)$/i;

//     const substitutions = [];
//     let firstNonRuleLineIndex = lines.length;

//     for (let i = 0; i < lines.length; i++) {
//       const line = lines[i].trim();
//       const m = rule.exec(line);
//       if (!m) {
//         firstNonRuleLineIndex = i;
//         break;
//       }

//       let { pat, rep, flags } = m.groups;

//       // Handle common escape sequences in replacement
//       rep = rep
//         .replace(/\\n/g, "\n")
//         .replace(/\\r/g, "\r")
//         .replace(/\\t/g, "\t")
//         .replace(/\\\\/g, "\\")
//         .replace(/\\\$/g, "$");

//       substitutions.push({
//         rx: new RegExp(pat, flags || undefined),
//         rep
//       });
//     }

//     // Remaining text to apply substitutions on
//     const textBody = lines.slice(firstNonRuleLineIndex).join("\n");

//     let result = textBody;
//     for (const { rx, rep } of substitutions) {
//       result = result.replace(rx, rep);
//     }

//     state.text = result;
//   } catch (err) {
//     state.postError("Regex substitution failed.");
//     state.text += "\n" + err.message;
//   }
// }


// Script          ::= Definitions Rules TextBody?

// Definitions     ::= Definition*
// Definition      ::= "def" Identifier Delim Value Delim

// Rules           ::= Rule*
// Rule            ::= "s" Delim Pattern Delim Replacement Delim Flags?

// Identifier      ::= Alpha ( Alpha | Digit | "_" )*

// Value           ::= ( EscapedChar | AnyCharExcept(Delim) )*

// Pattern         ::= ( EscapedChar | AnyCharExcept(Delim) )+
// Replacement     ::= ( EscapedChar | AnyCharExcept(Delim) )*
// Flags           ::= [a-z]*

// EscapedChar     ::= "\" AnyChar
// AnyCharExcept(x)::= AnyChar - x
// Delim           ::= AnyNonAlphanumericNonWhitespace
// Alpha           ::= "A".."Z" | "a".."z"
// Digit           ::= "0".."9"

// TextBody        ::= /* everything after last rule */
// AnyChar         ::= /* any Unicode codepoint */


// Script ::= ScriptElement*

// ScriptElement ::= NestedBlock
//                 | Definition
//                 | Rule
//                 | TextLine


// // ==========================
// // Definitions
// // ==========================
// Definition  ::= "def" WS* Identifier WS* Delim Value Delim
// Identifier  ::= Alpha (Alpha | Digit | "_")*

// Value       ::= (EscapedChar | AnyCharExcept(Delim))*


// // ==========================
// // Regex Substitution Rules
// // ==========================
// Rule        ::= "s"
//                 Delim Pattern Delim Replacement Delim Flags?

// Pattern     ::= (EscapedChar | AnyCharExcept(Delim))+
// Replacement ::= (EscapedChar | AnyCharExcept(Delim))*
// Flags       ::= [a-z]*


// // ==========================
// // Nested Blocks
// // ==========================
// NestedBlock ::= "<" BlockFlags? "<<" InnerScript LoopSuffix

// BlockFlags  ::= [sd]{0,2}

// LoopSuffix  ::= ">>>"                                 // simple nested block
//               | ">" LoopValues ">" LoopVar ">"        // loop block

// InnerScript ::= /* raw text until the closing >> or >(... )>var> */


// // --------------------------
// // Loop Structures
// // --------------------------
// LoopValues  ::= "(" LoopValueList? ")"
// LoopValueList ::= LoopValue ("," LoopValue)*
// LoopValue   ::= Identifier | Number | StringLiteral?   // flexible

// LoopVar     ::= Identifier


// // ==========================
// // Lexical
// // ==========================
// Delim          ::= AnyNonAlphanumericNonWhitespace
// EscapedChar    ::= "\\" AnyChar
// AnyChar        ::= /* any Unicode character */
// AnyCharExcept(x)::= AnyChar - x
// Comment        ::= "//" /* until end of line */
// TextLine       ::= /* any line not matched above */
// WS             ::= " " | "\t"
// Alpha          ::= "A".."Z" | "a".."z"
// Digit          ::= "0".."9"


const DEBUG = false;

function main(state) {
    const nestedRegex = /<(?<nflags>[sd]{0,2})<<(?<inner>.*)>(?:(?<loopvals>\((?:\S+(?<!\\)[,\)])*?)>(?<loopvar>[A-Za-z0-9_]+)>|>>)/gs;
    const definitions = {};
    const substitutions = [];
    state.nflags = "";
    try {
        const initInput = state.text
        const nestedCheckResults = initInput.replaceAll(nestedRegex, (m, ...args) => {
            const { inner, nflags, loopvals, loopvar } = args.slice(-1)[0];
            if (loopvals && loopvar) {
                const vals = loopvals.slice(1, -1).split(",").map(ele => ele.trim());
                let combinedResult = "";
                for (const val of vals) {
                    const replacedInner = inner.replaceAll(new RegExp(`{${loopvar}}`, "g"), val);
                    const newState = { text: replacedInner, postError: state.postError };
                    main(newState);
                    combinedResult += newState.text;
                }
                Object.assign(state, { nflags: nflags });
                return combinedResult;
            }
            const newState = { text: inner, postError: state.postError, nested: true };
            main(newState);
            Object.assign(state, { nflags: nflags, definitions: newState.definitions, substitutions: newState.substitutions });
            return newState.text;
        });
        state.text = nestedCheckResults;
        if (state.nflags.includes("d")) Object.assign(definitions, state.definitions);
        if (state.nflags.includes("s")) substitutions.push(...state.substitutions);
    } catch (err) {
        state.postError("Regex substitution failed.");
        state.text += "\n" + err.message;
    }
    try {
        const lines = state.text.split(/\r?\n/);
        // lines.push("EOF");
        const ruleRegex =
            /^s(?<d>[^A-Za-z0-9\s])(?<pat>(?:\\.|(?!\k<d>).)+)\k<d>(?<rep>(?:\\.|(?!\k<d>).)*)\k<d>(?<flags>[a-z]*)$/i;

        const defRegex =
            /^def\s+(?<id>[A-Za-z][A-Za-z0-9_]*)\s*(?<d>[^A-Za-z0-9\s])(?<val>(?:\\.|(?!\k<d>).)*)\k<d>(?:$|\r?\n)/s;


        const headers = [];

        let firstNonRuleLineIndex = lines.length;

        // --- Parse definitions and rules ---
        for (let i = 0; i < lines.length; i++) {
            let line = lines[i].trim();
            // line = expand(line, definitions);
            
            // --- NEW: skip blank lines while parsing rules/defs
            if (line === "") {
                continue;
            }
            if (line.startsWith("//")) {
                continue;
            }
            const remainingLines = lines.slice(i).join("\n");
            const defMatch = defRegex.exec(remainingLines);
            if (defMatch) {
                headers.push(expand(defMatch[0].trim(), definitions));
                // headers.push(line);
                const { id, val } = defMatch.groups;
                definitions[id] = expand(val, definitions);
                // definitions[id] = val;
                const defLineCount = defMatch[0].trim().split(/\r?\n/).length;
                i += defLineCount - 1;
                continue;
            }

            const ruleMatch = ruleRegex.exec(line);
            if (ruleMatch) {
                headers.push(expand(line, definitions));
                let { d, pat, rep, flags } = ruleMatch.groups;
                pat = expand(pat, definitions);
                rep = expand(rep, definitions);
                flags = expand(flags, definitions);

                rep = rep
                    .replace(/\\n/g, "\n")
                    .replace(/\\r/g, "\r")
                    .replace(/\\t/g, "\t")
                    .replace(/\\\\/g, "\\")
                    .replace(/\\\$/g, "$");

                substitutions.push({
                    rx: new RegExp(pat, flags || undefined),
                    rep
                });

                continue;
            }

            // First non-rule, non-def line → body begins
            firstNonRuleLineIndex = i;
            break;
        }

        // --- The body of text to transform ---
        let textBody = lines.slice(firstNonRuleLineIndex).join("\n");

        // --- Apply substitutions ---
        const runDebug = (initInput,finalOutput) => {
            let debugTxt = "\n";
            // debugTxt += "\n===\n" + "Initial Input:\n" + initInput;
            debugTxt += "\n===\n" + "After Nested Processing:\n" + finalOutput;
            debugTxt += "\n===\n" + JSON.stringify(definitions);
            debugTxt += "\n===\n" + JSON.stringify(substitutions.map(ele=>{return {...ele,...{rx: ele.rx.toString()}}}));
            debugTxt += "\n===\n" + "FirstNonRuleLineIndex: " + firstNonRuleLineIndex + "\n";
            debugTxt = "\n"+debugTxt.split("\n").map(lines=>"// "+lines).join("\n");
            return finalOutput += debugTxt;
        }
        // substitutions.push({
        //     rx: /EOF/g,
        //     rep: ""
        // });
        if (substitutions.length === 0 && !state.retry) {
            const initInput = state.text;
            textBody = expand(textBody, definitions);
            const newState = { text: textBody, postError: state.postError, retry: true};
            main(newState);
            if (DEBUG) newState.text = runDebug(initInput, newState.text);
            if (state.nested) {
                state.text = newState.text;
            } else {
                state.text = headers.join("\n") + "\n" + newState.text;
            }
            state.definitions = definitions;
            state.substitutions = substitutions;
            return;
        }
        const runSubstitutions = (inputText, subs) => {
            let outputText = inputText;
            for (const { rx, rep } of subs) {
                outputText = outputText.replace(rx, rep);
            }
            return outputText;
        };
        const initInput = state.text;
        let finalOutput = runSubstitutions(textBody, substitutions);
        if (DEBUG) finalOutput = runDebug(initInput, finalOutput);
        if (state.nested) {
            state.text = finalOutput;
        } else {
            state.text = headers.join("\n") + "\n" + finalOutput;
        }
        state.definitions = definitions;
        state.substitutions = substitutions;
    } catch (err) {
        state.postError("Regex substitution failed.");
        state.text += "\n" + err.message;
    }
}

// Macro expander (simple literal replace)
function expand(str, defs) {
    let last;
    let safety = 0;

    do {
        last = str;
        for (const id in defs) {
            str = str.replaceAll(id, defs[id]);
        }
        safety++;
        if (safety > 50) break;  // prevent runaway recursion
    } while (str !== last);

    return str;
}

