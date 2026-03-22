'use client';

import { useEffect, useState, useCallback } from 'react';
import {
  DndContext,
  DragEndEvent,
  DragOverlay,
  DragStartEvent,
  PointerSensor,
  useSensor,
  useSensors,
  closestCenter,
} from '@dnd-kit/core';
import { Header } from '@/components/layout/header';
import { KanbanColumn } from '@/components/kanban/column';
import { KanbanCard } from '@/components/kanban/card';
import { TaskModal } from '@/components/kanban/task-modal';
import { Button } from '@/components/ui/button';
import { kanbanApi, KanbanTask, CreateTaskRequest } from '@/lib/api';
import { Plus, RefreshCw } from 'lucide-react';

const LANES = [
  { id: 'ToDo', title: 'To Do', color: 'bg-gray-100' },
  { id: 'InProgress', title: 'In Progress', color: 'bg-blue-50' },
  { id: 'NeedsApproval', title: 'Needs Approval', color: 'bg-yellow-50' },
  { id: 'Done', title: 'Done', color: 'bg-green-50' },
];

export default function KanbanPage() {
  const [tasks, setTasks] = useState<KanbanTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [activeTask, setActiveTask] = useState<KanbanTask | null>(null);
  const [selectedTask, setSelectedTask] = useState<KanbanTask | null>(null);
  const [showNewTaskModal, setShowNewTaskModal] = useState(false);

  const sensors = useSensors(
    useSensor(PointerSensor, {
      activationConstraint: {
        distance: 8,
      },
    })
  );

  const loadTasks = useCallback(async () => {
    try {
      setLoading(true);
      const response = await kanbanApi.getTasks();
      setTasks(response.tasks || []);
    } catch (error) {
      console.error('Failed to load tasks:', error);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadTasks();
  }, [loadTasks]);

  const handleDragStart = (event: DragStartEvent) => {
    const task = tasks.find((t) => t.id === event.active.id);
    setActiveTask(task || null);
  };

  const handleDragEnd = async (event: DragEndEvent) => {
    const { active, over } = event;
    setActiveTask(null);

    if (!over) return;

    const taskId = active.id as string;
    const newLane = over.id as string;

    const task = tasks.find((t) => t.id === taskId);
    if (!task || task.lane === newLane) return;

    // Optimistic update
    setTasks((prev) =>
      prev.map((t) => (t.id === taskId ? { ...t, lane: newLane } : t))
    );

    try {
      await kanbanApi.moveTask(taskId, newLane);
    } catch (error) {
      console.error('Failed to move task:', error);
      // Revert on error
      loadTasks();
    }
  };

  const handleApprove = async (taskId: string) => {
    try {
      await kanbanApi.approveTask(taskId);
      loadTasks();
    } catch (error) {
      console.error('Failed to approve task:', error);
    }
  };

  const handleReject = async (taskId: string, reason?: string) => {
    try {
      await kanbanApi.rejectTask(taskId, reason);
      loadTasks();
    } catch (error) {
      console.error('Failed to reject task:', error);
    }
  };

  const handleDeleteTask = async (taskId: string) => {
    try {
      await kanbanApi.deleteTask(taskId);
      setTasks((prev) => prev.filter((t) => t.id !== taskId));
      setSelectedTask(null);
    } catch (error) {
      console.error('Failed to delete task:', error);
    }
  };

  const handleCreateTask = async (data: CreateTaskRequest) => {
    try {
      const task = await kanbanApi.createTask(data);
      setTasks((prev) => [...prev, task]);
      setShowNewTaskModal(false);
    } catch (error) {
      console.error('Failed to create task:', error);
    }
  };

  const getTasksForLane = (laneId: string) =>
    tasks.filter((task) => task.lane === laneId);

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" />
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full">
      <Header title="Kanban Board" description="Manage tasks and approvals" />

      <div className="p-4 border-b border-gray-200 bg-white flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Button onClick={() => setShowNewTaskModal(true)}>
            <Plus className="h-4 w-4 mr-2" />
            New Task
          </Button>
        </div>
        <Button variant="outline" onClick={loadTasks}>
          <RefreshCw className="h-4 w-4 mr-2" />
          Refresh
        </Button>
      </div>

      <div className="flex-1 p-6 overflow-x-auto">
        <DndContext
          sensors={sensors}
          collisionDetection={closestCenter}
          onDragStart={handleDragStart}
          onDragEnd={handleDragEnd}
        >
          <div className="flex gap-4 h-full min-w-max">
            {LANES.map((lane) => (
              <KanbanColumn
                key={lane.id}
                id={lane.id}
                title={lane.title}
                color={lane.color}
                tasks={getTasksForLane(lane.id)}
                onTaskClick={setSelectedTask}
                onApprove={handleApprove}
                onReject={handleReject}
              />
            ))}
          </div>

          <DragOverlay>
            {activeTask ? (
              <KanbanCard
                task={activeTask}
                isDragging
                onClick={() => {}}
              />
            ) : null}
          </DragOverlay>
        </DndContext>
      </div>

      {selectedTask && (
        <TaskModal
          task={selectedTask}
          onClose={() => setSelectedTask(null)}
          onApprove={() => handleApprove(selectedTask.id)}
          onReject={(reason) => handleReject(selectedTask.id, reason)}
          onDelete={() => handleDeleteTask(selectedTask.id)}
        />
      )}

      {showNewTaskModal && (
        <TaskModal
          onClose={() => setShowNewTaskModal(false)}
          onCreate={handleCreateTask}
        />
      )}
    </div>
  );
}
