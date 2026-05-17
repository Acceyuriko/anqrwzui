import argparse
import copy
import json
import os
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

import httpx
from label_studio_sdk.client import LabelStudio

# 也可以用环境变量覆盖这些默认值：LABEL_STUDIO_URL / LABEL_STUDIO_API_KEY / LABEL_STUDIO_PROJECT_ID
LS_URL = os.getenv('LABEL_STUDIO_URL', 'http://localhost:8080')
API_KEY = os.getenv(
    'LABEL_STUDIO_API_KEY',
    'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ0b2tlbl90eXBlIjoicmVmcmVzaCIsImV4cCI6ODA4NTYxMjU1MiwiaWF0IjoxNzc4NDEyNTUyLCJqdGkiOiI5Nzk5MjgzNDgwOTM0MDM3OGY2ZTFiMTZlNGEzMTM5OSIsInVzZXJfaWQiOiIxIn0.UnhHFe0bYLW4csVDNJ2qEodtiDECjtFsR138bbLo6Fk',
)
PROJECT_ID = os.getenv('LABEL_STUDIO_PROJECT_ID', '1')


def utc_timestamp():
    return datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')


def default_output_path(prefix):
    return Path(__file__).resolve().with_name(f'{prefix}_{utc_timestamp()}.json')


def mask_secret(secret):
    if not secret:
        return ''
    if len(secret) <= 10:
        return '*' * len(secret)
    return f'{secret[:6]}...{secret[-4:]}'


def parse_task_ids(raw_values):
    task_ids = []
    for raw_value in raw_values or []:
        for chunk in str(raw_value).split(','):
            chunk = chunk.strip()
            if not chunk:
                continue
            task_ids.append(int(chunk))
    return task_ids


def normalize_project_id(project_id):
    return int(project_id) if str(project_id).isdigit() else project_id


def build_client():
    httpx_client = httpx.Client(trust_env=False, timeout=60.0)
    ls = LabelStudio(base_url=LS_URL, api_key=API_KEY, httpx_client=httpx_client)
    return ls, httpx_client


class PrivateApiClient:
    def __init__(self, wrapper):
        self._wrapper = wrapper
        self._client = httpx.Client(
            base_url=wrapper.get_base_url(),
            trust_env=False,
            timeout=60.0,
        )

    def request(self, method, path, **kwargs):
        headers = dict(self._wrapper.get_headers())
        extra_headers = kwargs.pop('headers', None) or {}
        headers.update(extra_headers)
        return self._client.request(method, path, headers=headers, **kwargs)

    def delete(self, path, **kwargs):
        return self.request('DELETE', path, **kwargs)

    def close(self):
        self._client.close()


def build_private_client(ls):
    wrapper = ls.annotations._raw_client._client_wrapper
    return PrivateApiClient(wrapper)


def iter_selected_tasks(ls, project_id, task_ids, limit):
    if task_ids:
        for task_id in task_ids:
            yield ls.tasks.get(task_id)
        return

    count = 0
    for task in ls.tasks.list(project=project_id):
        yield task
        count += 1
        if limit and count >= limit:
            return


def annotation_field(annotation, field_name, default=None):
    if isinstance(annotation, dict):
        return annotation.get(field_name, default)
    return getattr(annotation, field_name, default)


def iter_task_annotations(ls, task):
    embedded_annotations = list(getattr(task, 'annotations', []) or [])
    if embedded_annotations:
        return embedded_annotations
    return list(ls.annotations.list(task.id))


def snapshot_annotation(annotation):
    result = list(annotation_field(annotation, 'result', []) or [])
    return {
        'annotation_id': annotation_field(annotation, 'id'),
        'task_id': annotation_field(annotation, 'task'),
        'was_cancelled': bool(annotation_field(annotation, 'was_cancelled', False)),
        'state': annotation_field(annotation, 'state', None),
        'ground_truth': annotation_field(annotation, 'ground_truth', None),
        'created_at': annotation_field(annotation, 'created_at', None),
        'updated_at': annotation_field(annotation, 'updated_at', None),
        'draft_created_at': annotation_field(annotation, 'draft_created_at', None),
        'lead_time': annotation_field(annotation, 'lead_time', None),
        'created_username': annotation_field(annotation, 'created_username', None),
        'updated_by': annotation_field(annotation, 'updated_by', None),
        'parent_annotation': annotation_field(annotation, 'parent_annotation', None),
        'parent_prediction': annotation_field(annotation, 'parent_prediction', None),
        'import_id': annotation_field(annotation, 'import_id', None),
        'last_action': annotation_field(annotation, 'last_action', None),
        'result_count': len(result),
        'has_result': bool(result),
        'result': result,
    }


def snapshot_prediction(prediction):
    result = list(annotation_field(prediction, 'result', []) or [])
    return {
        'prediction_id': annotation_field(prediction, 'id'),
        'task_id': annotation_field(prediction, 'task'),
        'model_version': annotation_field(prediction, 'model_version', None),
        'score': annotation_field(prediction, 'score', None),
        'created_at': annotation_field(prediction, 'created_at', None),
        'updated_at': annotation_field(prediction, 'updated_at', None),
        'result_count': len(result),
        'has_result': bool(result),
        'result': result,
    }


def snapshot_draft(draft):
    result = list(annotation_field(draft, 'result', []) or [])
    return {
        'draft_id': annotation_field(draft, 'id'),
        'task_id': annotation_field(draft, 'task'),
        'annotation_id': annotation_field(draft, 'annotation'),
        'user': annotation_field(draft, 'user', None),
        'created_username': annotation_field(draft, 'created_username', None),
        'created_at': annotation_field(draft, 'created_at', None),
        'updated_at': annotation_field(draft, 'updated_at', None),
        'lead_time': annotation_field(draft, 'lead_time', None),
        'was_postponed': bool(annotation_field(draft, 'was_postponed', False)),
        'import_id': annotation_field(draft, 'import_id', None),
        'result_count': len(result),
        'has_result': bool(result),
        'result': result,
    }


def snapshot_task(task, annotations, predictions, drafts):
    return {
        'task_id': task.id,
        'state': getattr(task, 'state', None),
        'is_labeled': getattr(task, 'is_labeled', None),
        'total_annotations': getattr(task, 'total_annotations', None),
        'cancelled_annotations': getattr(task, 'cancelled_annotations', None),
        'draft_count': len(drafts),
        'prediction_count': len(predictions),
        'data': getattr(task, 'data', None),
        'meta': getattr(task, 'meta', None),
        'annotations': [snapshot_annotation(annotation) for annotation in annotations],
        'predictions': [snapshot_prediction(prediction) for prediction in predictions],
        'drafts': [snapshot_draft(draft) for draft in drafts],
    }


def collect_task_snapshots(ls, project_id, task_ids, limit, verbose):
    summary = Counter()
    task_snapshots = []

    for task in iter_selected_tasks(ls, project_id, task_ids, limit):
        annotations = iter_task_annotations(ls, task)
        predictions = list(getattr(task, 'predictions', []) or [])
        drafts = list(getattr(task, 'drafts', []) or [])
        task_snapshot = snapshot_task(task, annotations, predictions, drafts)
        task_snapshots.append(task_snapshot)

        summary['tasks_scanned'] += 1
        summary['tasks_with_drafts'] += int(task_snapshot['draft_count'] > 0)
        summary['tasks_with_annotations'] += int(bool(task_snapshot['annotations']))
        summary['tasks_without_annotations'] += int(not task_snapshot['annotations'])
        summary['tasks_with_predictions'] += int(task_snapshot['prediction_count'] > 0)
        summary['drafts_total'] += task_snapshot['draft_count']
        summary['predictions_total'] += task_snapshot['prediction_count']

        for annotation in task_snapshot['annotations']:
            summary['annotations_total'] += 1
            summary['annotations_cancelled'] += int(annotation['was_cancelled'])
            summary['annotations_with_result'] += int(annotation['has_result'])
            summary['annotations_empty_result'] += int(not annotation['has_result'])

        if verbose:
            print(
                f"任务 {task_snapshot['task_id']}: 标注 {len(task_snapshot['annotations'])} 条, "
                f"cancelled={task_snapshot['cancelled_annotations']}, drafts={task_snapshot['draft_count']}, "
                f"predictions={task_snapshot['prediction_count']}"
            )

    return task_snapshots, summary


def write_json(path, payload):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('w', encoding='utf-8') as handle:
        json.dump(payload, handle, ensure_ascii=False, indent=2, default=str)


def read_json(path):
    with path.open('r', encoding='utf-8') as handle:
        return json.load(handle)


def build_backup_payload(command, args, summary, task_snapshots):
    return {
        'generated_at': datetime.now(timezone.utc).isoformat(),
        'command': command,
        'project_id': args.project_id,
        'task_ids': args.task_ids,
        'all_task': bool(args.all_task),
        'base_url': LS_URL,
        'api_key_masked': mask_secret(API_KEY),
        'summary_before': dict(summary),
        'tasks': task_snapshots,
    }


def draft_result_from_source(result):
    draft_result = copy.deepcopy(result)
    for item in draft_result:
        if item.get('origin') == 'prediction':
            item['origin'] = 'manual'
    return draft_result


def request_json(client, method, path, **kwargs):
    response = client.request(method, path, **kwargs)
    if response.status_code >= 400:
        raise RuntimeError(f'{method} {path} failed: {response.status_code} {response.text[:300]}')
    if not response.content:
        return None
    return response.json()


def request_delete(client, path):
    response = client.delete(path)
    if response.status_code in (200, 204, 404):
        return
    raise RuntimeError(f'DELETE {path} failed: {response.status_code} {response.text[:300]}')


def get_live_task_state(private_client, task_id):
    payload = request_json(private_client, 'GET', f'/api/tasks/{task_id}/')
    return {
        'annotations': [snapshot_annotation(annotation) for annotation in payload.get('annotations', []) or []],
        'predictions': [snapshot_prediction(prediction) for prediction in payload.get('predictions', []) or []],
        'drafts': [snapshot_draft(draft) for draft in payload.get('drafts', []) or []],
    }


def clear_live_task_state(private_client, task_id, live_state):
    for draft in live_state['drafts']:
        request_delete(private_client, f"/api/drafts/{draft['draft_id']}/")

    for annotation in live_state['annotations']:
        request_delete(private_client, f"/api/annotations/{annotation['annotation_id']}/")

    for prediction in live_state['predictions']:
        request_delete(private_client, f"/api/predictions/{prediction['prediction_id']}/")


def build_restore_sources(task_snapshot):
    collections = [
        ('annotation', task_snapshot.get('annotations', []), 'annotation_id'),
        ('draft', task_snapshot.get('drafts', []), 'draft_id'),
        ('prediction', task_snapshot.get('predictions', []), 'prediction_id'),
    ]

    for source_type, items, id_field in collections:
        sources = []
        for item in items:
            if not item.get('has_result'):
                continue
            sources.append(
                {
                    'source_type': source_type,
                    'source_id': item.get(id_field),
                    'result_count': item['result_count'],
                    'lead_time': item.get('lead_time'),
                    'result': item['result'],
                }
            )
        if sources:
            return sources

    return []


def create_draft_from_source(private_client, task_id, source):
    payload = {
        'result': draft_result_from_source(source['result']),
        'lead_time': source.get('lead_time'),
        'was_postponed': False,
    }
    draft = request_json(private_client, 'POST', f'/api/tasks/{task_id}/drafts', json=payload)
    return {
        'status': 'created',
        'draft_id': draft['id'],
    }


def iter_backup_tasks(backup_payload, task_ids):
    selected_ids = set(task_ids or [])
    for task_snapshot in backup_payload.get('tasks', []):
        task_id = task_snapshot.get('task_id')
        if selected_ids and task_id not in selected_ids:
            continue
        yield task_snapshot


def plan_or_execute_restore(private_client, backup_tasks, args):
    action_summary = Counter()
    action_records = []

    for task_snapshot in backup_tasks:
        live_state = get_live_task_state(private_client, task_snapshot['task_id'])
        restore_sources = build_restore_sources(task_snapshot)

        action_summary['tasks_scanned'] += 1
        action_summary['live_annotations_found'] += len(live_state['annotations'])
        action_summary['live_predictions_found'] += len(live_state['predictions'])
        action_summary['live_drafts_found'] += len(live_state['drafts'])

        if not restore_sources:
            action_summary['tasks_without_backup_result'] += 1
            action_records.append(
                {
                    'task_id': task_snapshot['task_id'],
                    'status': 'missing_backup_result',
                    'reason': 'The backup snapshot contains no restorable annotation/draft/prediction result for this task.',
                    'live_annotation_count': len(live_state['annotations']),
                    'live_prediction_count': len(live_state['predictions']),
                    'live_draft_count': len(live_state['drafts']),
                }
            )
            continue

        try:
            clear_live_task_state(private_client, task_snapshot['task_id'], live_state)
            action_summary['tasks_cleared'] += 1
            action_summary['annotations_deleted'] += len(live_state['annotations'])
            action_summary['predictions_deleted'] += len(live_state['predictions'])
            action_summary['drafts_deleted'] += len(live_state['drafts'])
        except Exception as exc:
            action_summary['clear_failures'] += 1
            action_records.append(
                {
                    'task_id': task_snapshot['task_id'],
                    'status': 'clear_failed',
                    'error': str(exc),
                    'live_annotation_count': len(live_state['annotations']),
                    'live_prediction_count': len(live_state['predictions']),
                    'live_draft_count': len(live_state['drafts']),
                }
            )
            continue

        for source in restore_sources:
            record = {
                'task_id': task_snapshot['task_id'],
                'source_type': source['source_type'],
                'source_id': source['source_id'],
                'result_count': source['result_count'],
            }
            try:
                draft_outcome = create_draft_from_source(private_client, task_snapshot['task_id'], source)
                record['status'] = 'restored_to_draft'
                record['draft_status'] = draft_outcome['status']
                record['draft_id'] = draft_outcome['draft_id']
                action_summary['drafts_created'] += 1
            except Exception as exc:
                record['status'] = 'restore_failed'
                record['error'] = str(exc)
                action_summary['restore_failures'] += 1
            action_records.append(record)

    return action_records, action_summary


def print_summary(title, summary):
    print(title)
    for key in sorted(summary):
        print(f'  - {key}: {summary[key]}')


def find_latest_backup_file():
    env_file = os.getenv('LABEL_STUDIO_BACKUP_FILE')
    if env_file:
        return Path(env_file)
    backup_files = sorted(Path(__file__).resolve().parent.glob('label_studio_backup_*.json'))
    if not backup_files:
        raise FileNotFoundError('未找到备份文件。请先运行 backup 命令生成备份。')
    return backup_files[-1]


def build_parser():
    parser = argparse.ArgumentParser(
        description='备份或恢复 Label Studio 任务。backup 会导出当前任务快照；restore 会从最新备份恢复指定任务。'
    )
    parser.add_argument('command', nargs='?', choices=['backup', 'restore'], default='backup')
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument('--task-ids', nargs='*', default=[], help='只处理指定任务，支持空格分隔或逗号分隔')
    group.add_argument('--all-task', action='store_true', help='显式确认要处理整个项目')
    parser.add_argument('--project-id', default=PROJECT_ID, help='默认使用文件或环境变量中的项目 ID')
    parser.add_argument('--verbose', action='store_true', help='输出逐任务进度')
    return parser


def main():
    parser = build_parser()
    args = parser.parse_args()
    args.task_ids = parse_task_ids(args.task_ids)
    args.project_id = normalize_project_id(args.project_id)

    ls, httpx_client = build_client()
    private_client = build_private_client(ls)
    try:
        if args.command == 'backup':
            task_snapshots, summary = collect_task_snapshots(
                ls=ls,
                project_id=args.project_id,
                task_ids=args.task_ids,
                limit=None,
                verbose=args.verbose,
            )
            backup_path = default_output_path('label_studio_backup')
            backup_payload = build_backup_payload('backup', args, summary, task_snapshots)
            write_json(backup_path, backup_payload)
            print(f'备份已写入: {backup_path}')
            print_summary('backup 摘要：', summary)
            return 0

        backup_path = find_latest_backup_file()
        backup_payload = read_json(backup_path)
        backup_tasks = list(iter_backup_tasks(backup_payload, args.task_ids))

        action_records, action_summary = plan_or_execute_restore(private_client, backup_tasks, args)
        print_summary('restore 执行摘要：', action_summary)
        print(f'恢复操作已完成，使用备份文件: {backup_path}')
        return 0
    finally:
        private_client.close()
        httpx_client.close()
