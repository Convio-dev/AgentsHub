/**
 * RPC Message Router
 * Routes incoming host actions to tool executors, task queue, and terminal sessions.
 */

import { InboundMessage, OutboundEvent } from '../types/protocol.ts';
import { TaskQueue } from '../queue/taskQueue.ts';
import { ShellExecutor } from '../tools/executor.ts';
import { FsTools } from '../tools/fsTools.ts';
import { PtyRunner } from '../tools/ptyRunner.ts';

export class MessageRouter {
  private queue: TaskQueue;
  private pty: PtyRunner;
  private emit: (event: OutboundEvent) => void;

  constructor(queue: TaskQueue, pty: PtyRunner, emit: (event: OutboundEvent) => void) {
    this.queue = queue;
    this.pty = pty;
    this.emit = emit;
  }

  public async handleMessage(msg: InboundMessage): Promise<void> {
    switch (msg.action) {
      case 'ping':
        this.emit({
          event: 'pong',
          replyTo: msg.id,
          timestamp: new Date().toISOString()
        });
        break;

      case 'start_task':
        this.handleStartTask(msg);
        break;

      case 'abort_task':
        this.queue.abort(msg.taskId, msg.reason);
        break;

      case 'execute_tool':
        await this.handleExecuteTool(msg);
        break;

      case 'terminal_input':
        this.pty.writeInput(msg.sessionId, msg.data);
        break;

      case 'terminal_resize':
        this.pty.resize(msg.sessionId, msg.cols, msg.rows);
        break;

      case 'shutdown':
        process.exit(0);
        break;

      default:
        this.emit({
          event: 'error',
          replyTo: (msg as any).id,
          code: 'UNKNOWN_ACTION',
          message: `Unknown action: ${(msg as any).action}`,
          timestamp: new Date().toISOString()
        });
        break;
    }
  }

  private handleStartTask(msg: any): void {
    this.queue.enqueue({
      taskId: msg.taskId,
      prompt: msg.prompt,
      workingDirectory: msg.workingDirectory,
      environment: msg.environment,
      execute: async (ctx) => {
        // Sample workflow execution loop emitting streaming deltas
        ctx.setState('thinking', 'Analyzing instructions...');
        await Bun.sleep(100);

        ctx.setState('streaming');
        const chunks = [
          'Starting sidecar agent execution...\n',
          `Working Directory: ${msg.workingDirectory || process.cwd()}\n`,
          'Task execution initiated via Bun compiled runtime.\n'
        ];

        for (const chunk of chunks) {
          if (ctx.abortSignal.aborted) break;
          ctx.streamText(chunk);
          await Bun.sleep(50);
        }

        return { processed: true };
      }
    });
  }

  private async handleExecuteTool(msg: any): Promise<void> {
    const startTime = performance.now();
    const { callId, taskId, tool, params, timeoutMs } = msg;

    this.emit({
      event: 'tool_start',
      taskId: taskId || 'standalone',
      callId,
      tool,
      params,
      timestamp: new Date().toISOString()
    });

    try {
      let result: any = null;

      switch (tool) {
        case 'shell_exec': {
          const res = await ShellExecutor.execute({
            command: params.command,
            cwd: params.cwd,
            env: params.env,
            timeoutMs,
            onStdout: (chunk) => {
              this.emit({
                event: 'stream_delta',
                taskId: taskId || 'standalone',
                text: chunk,
                index: 0,
                timestamp: new Date().toISOString()
              });
            }
          });
          result = res;
          break;
        }

        case 'file_read': {
          result = await FsTools.readFile(params.path);
          break;
        }

        case 'file_write': {
          result = await FsTools.writeFileAtomic(params.path, params.content);
          break;
        }

        case 'file_patch': {
          result = await FsTools.applyDiffPatch(params.path, params.patch);
          break;
        }

        case 'dir_tree': {
          result = await FsTools.getDirectoryTree(params.path, params.maxDepth, params.ignores);
          break;
        }

        case 'pty_session': {
          this.pty.spawnSession({
            sessionId: params.sessionId,
            command: params.command,
            cwd: params.cwd,
            cols: params.cols,
            rows: params.rows,
            env: params.env,
            onData: (data) => {
              this.emit({
                event: 'terminal_data',
                sessionId: params.sessionId,
                data,
                timestamp: new Date().toISOString()
              });
            },
            onExit: (code) => {
              this.emit({
                event: 'tool_output',
                taskId: taskId || 'standalone',
                callId,
                tool: 'pty_session',
                success: code === 0,
                exitCode: code,
                durationMs: Math.round(performance.now() - startTime),
                timestamp: new Date().toISOString()
              });
            }
          });
          result = { spawned: true, sessionId: params.sessionId };
          break;
        }

        default:
          throw new Error(`Unsupported tool: ${tool}`);
      }

      this.emit({
        event: 'tool_output',
        taskId: taskId || 'standalone',
        callId,
        tool,
        success: true,
        result,
        durationMs: Math.round(performance.now() - startTime),
        timestamp: new Date().toISOString()
      });
    } catch (err: any) {
      this.emit({
        event: 'tool_output',
        taskId: taskId || 'standalone',
        callId,
        tool,
        success: false,
        error: err?.message || String(err),
        durationMs: Math.round(performance.now() - startTime),
        timestamp: new Date().toISOString()
      });
    }
  }
}
