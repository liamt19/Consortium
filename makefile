PUBLISH_DIR := .\obj\publish

GENERAL_NAME   := Consortium.General
DEMOCRACY_NAME := Consortium.Democracy

GENERAL_PROJ   := $(GENERAL_NAME)/$(GENERAL_NAME).csproj
DEMOCRACY_PROJ := $(DEMOCRACY_NAME)/$(DEMOCRACY_NAME).csproj

ifeq ($(OS),Windows_NT)
	BINARY_SUFFIX = .exe
	MOVE_CMD = cmd /c move
	RM_FILE_CMD = cmd /c del
else
	BINARY_SUFFIX =
	MOVE_CMD = mv
	RM_FILE_CMD = rm
endif

GENERAL_BINARY_NAME   := $(GENERAL_NAME)$(BINARY_SUFFIX)
DEMOCRACY_BINARY_NAME := $(DEMOCRACY_NAME)$(BINARY_SUFFIX)

CLEAN_GENERAL_CMD   := -$(RM_FILE_CMD) $(GENERAL_BINARY_NAME)
CLEAN_DEMOCRACY_CMD := -$(RM_FILE_CMD) $(DEMOCRACY_BINARY_NAME)

ifeq ($(OS),Windows_NT)
	CLEAN_OBJS_CMD = -rmdir /s /q "$(PUBLISH_DIR)"
else
	CLEAN_OBJS_CMD = -rm -rf $(PUBLISH_DIR)
endif

BUILD_OPTS := -c Release --self-contained true -p:PublishSingleFile=true

.PHONY: release clean
release: clean publish-apps copy-binaries

publish-apps:
	dotnet publish $(GENERAL_PROJ) $(BUILD_OPTS) -o $(PUBLISH_DIR)
	dotnet publish $(DEMOCRACY_PROJ) $(BUILD_OPTS) -o $(PUBLISH_DIR)

copy-binaries:
	$(MOVE_CMD) "$(PUBLISH_DIR)\$(GENERAL_BINARY_NAME)" ".\$(GENERAL_BINARY_NAME)"
	$(MOVE_CMD) "$(PUBLISH_DIR)\$(DEMOCRACY_BINARY_NAME)" ".\$(DEMOCRACY_BINARY_NAME)"
	$(MOVE_CMD) "$(PUBLISH_DIR)\config.json" ".\config.json"

clean:
	dotnet clean
	$(CLEAN_OBJS_CMD)
	$(CLEAN_GENERAL_CMD)
	$(CLEAN_DEMOCRACY_CMD)
