
BUILD_OPTS := -c Release --self-contained true -p:PublishSingleFile=true -o ./

release:
	dotnet publish $(BUILD_OPTS)