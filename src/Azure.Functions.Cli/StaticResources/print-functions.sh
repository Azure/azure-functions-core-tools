#! /bin/sh

if [ -z "$AzureWebJobsScriptRoot" ]; then
    cd /home/site/wwwroot
else
    cd "$AzureWebJobsScriptRoot"
fi

echo '{'
echo '"hostJson":'
if [ -f "host.json" ]; then
    cat host.json
else
    echo '{ }'
fi

echo ','

echo '"functionsJson": {'

if [ -f "functions.metadata" ]; then
    awk '
    BEGIN { in_obj=0; name=""; obj=""; first=1 }
    !in_obj && /^  \{$/ { in_obj=1; obj=$0"\n"; next }
    in_obj && /^  \}[,]?$/ {
        obj=obj"  }"
        if (!first) printf ",\n"
        printf "\"%s\": %s\n", name, obj
        first=0; in_obj=0; name=""; obj=""
        next
    }
    in_obj {
        if (name=="" && /^    "name":/) {
            tmp=$0; sub(/^[[:space:]]*"name":[[:space:]]*"/,"",tmp); sub(/".*$/,"",tmp); name=tmp
        }
        obj=obj$0"\n"
    }
    ' functions.metadata
else
    for d in */; do
        d=$(echo $d | tr -d '/')
        if [ -f "${d}/function.json" ]; then
            echo "\"${d}\": "
            cat "${d}/function.json"
            echo ','
        fi
    done
fi

echo '}'
echo '}'
