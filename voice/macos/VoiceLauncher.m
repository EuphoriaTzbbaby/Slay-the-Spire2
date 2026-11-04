// LaunchServices gives this app its own microphone consent, independent of the game.
#import <AVFoundation/AVFoundation.h>
#import <Foundation/Foundation.h>
#include <signal.h>
#include <stdatomic.h>
#include <unistd.h>

static atomic_bool parentClosed = false;

static int fail(const char *code) {
    fprintf(stderr, "SPIRE_VOICE_ERROR:%s\n", code);
    fflush(stderr);
    return 1;
}

int main(int argc, const char *argv[]) {
    @autoreleasepool {
        NSArray<NSString *> *arguments = [[NSProcessInfo processInfo].arguments subarrayWithRange:NSMakeRange(1, argc - 1)];
        BOOL managed = [arguments containsObject:@"--managed"];
        NSPipe *input = [NSPipe pipe];
        NSTask *child = [[NSTask alloc] init];
        NSLock *lifecycle = [[NSLock alloc] init];
        if (managed) {
            dispatch_async(dispatch_get_global_queue(QOS_CLASS_UTILITY, 0), ^{
                // Only EOF matters. Never read the session token or record audio here.
                char byte;
                while (read(STDIN_FILENO, &byte, 1) > 0) {}
                atomic_store(&parentClosed, true);
                [lifecycle lock];
                [input.fileHandleForWriting closeFile];
                [lifecycle unlock];
                dispatch_after(dispatch_time(DISPATCH_TIME_NOW, 3 * NSEC_PER_SEC),
                    dispatch_get_global_queue(QOS_CLASS_UTILITY, 0), ^{
                    [lifecycle lock];
                    if (child.running) [child terminate];
                    [lifecycle unlock];
                    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, NSEC_PER_SEC),
                        dispatch_get_global_queue(QOS_CLASS_UTILITY, 0), ^{
                        [lifecycle lock];
                        if (child.running) kill(child.processIdentifier, SIGKILL);
                        [lifecycle unlock];
                    });
                });
            });
        }

        // Permission is requested before declaring the service ready, never by the game app.
        if (![arguments containsObject:@"--list-devices"]) {
            AVAuthorizationStatus status = [AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeAudio];
            if (status == AVAuthorizationStatusNotDetermined) {
                dispatch_semaphore_t decided = dispatch_semaphore_create(0);
                [AVCaptureDevice requestAccessForMediaType:AVMediaTypeAudio completionHandler:^(BOOL granted) {
                    dispatch_semaphore_signal(decided);
                }];
                while (dispatch_semaphore_wait(decided, dispatch_time(DISPATCH_TIME_NOW, 100 * NSEC_PER_MSEC)) != 0) {
                    if (atomic_load(&parentClosed)) return 0;
                }
                status = [AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeAudio];
            }
            if (atomic_load(&parentClosed)) return 0;
            if (status != AVAuthorizationStatusAuthorized) return fail("microphone_permission_denied");
        }

        NSURL *configURL = [[NSBundle mainBundle] URLForResource:@"runner" withExtension:@"json"];
        NSData *data = configURL ? [NSData dataWithContentsOfURL:configURL] : nil;
        NSDictionary *config = data ? [NSJSONSerialization JSONObjectWithData:data options:0 error:nil] : nil;
        NSString *executable = config[@"Executable"];
        NSArray *baseArguments = config[@"Arguments"];
        NSString *directory = config[@"WorkingDirectory"];
        if (![executable isKindOfClass:NSString.class] || !executable.isAbsolutePath ||
            ![baseArguments isKindOfClass:NSArray.class] || ![directory isKindOfClass:NSString.class] ||
            !directory.isAbsolutePath) return fail("invalid_installation");
        for (id argument in baseArguments) if (![argument isKindOfClass:NSString.class]) return fail("invalid_installation");
        child.executableURL = [NSURL fileURLWithPath:executable];
        child.currentDirectoryURL = [NSURL fileURLWithPath:directory];
        child.arguments = [baseArguments arrayByAddingObjectsFromArray:arguments];
        NSMutableDictionary *environment = [NSProcessInfo processInfo].environment.mutableCopy;
        environment[@"PYTHONUTF8"] = @"1";
        environment[@"PYTHONUNBUFFERED"] = @"1";
        child.environment = environment;
        child.standardInput = managed ? input.fileHandleForReading : [NSFileHandle fileHandleWithStandardInput];
        child.standardOutput = [NSFileHandle fileHandleWithStandardOutput];
        child.standardError = [NSFileHandle fileHandleWithStandardError];
        NSError *error = nil;
        [lifecycle lock];
        BOOL launched = !atomic_load(&parentClosed) && [child launchAndReturnError:&error];
        [lifecycle unlock];
        if (!launched) return atomic_load(&parentClosed) ? 0 : fail("invalid_installation");
        [child waitUntilExit];
        return child.terminationStatus;
    }
}
